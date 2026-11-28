from datetime import date, datetime, timedelta, timezone
from pathlib import Path
import sys
import unittest
from zoneinfo import ZoneInfo

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "mac-bridge"))
from provider_task_views import (
    AccountTasks,
    AccessRevoked,
    ProviderOffline,
    Task,
    TaskList,
    TaskViews,
    ViewSelection,
)


UTC = timezone.utc
ZONE = ZoneInfo("Europe/Bratislava")


class TaskViewsTest(unittest.TestCase):
    def setUp(self):
        self.now = datetime(2026, 11, 27, 12, tzinfo=UTC)
        self.selection = ViewSelection("account", "inbox", ZONE, date(2026, 10, 1), date(2026, 11, 30))
        self.views = TaskViews(clock=lambda: self.now)
        self.views.select(self.selection)
        self.source = AccountTasks("account", (
            TaskList("inbox", "Saved", (
                Task("none", "Unscheduled note"),
                Task("id", "All-day task", date(2026, 11, 15), completed=True),
                Task("outside", "Later", date(2026, 12, 1)),
            )),
            TaskList("other", "Other list", (
                Task("id", "Local next day", datetime(2026, 11, 14, 23, 45, tzinfo=UTC)),
                Task("outside", "Previous month", date(2026, 9, 30)),
            )),
        ))

    def refresh(self):
        def read(account_id):
            self.assertEqual(account_id, "account")
            return self.source
        return self.views.refresh(read)

    def test_selected_list_order_and_account_wide_calendar(self):
        fresh = self.refresh()
        self.assertEqual(fresh.status, "online")
        self.assertEqual(fresh.updated_at, self.now)
        self.assertEqual(fresh.saved_list_title, "Saved")
        self.assertEqual([row.task_id for row in fresh.saved_items], ["none", "outside"])
        self.assertEqual([row.list_id for row in fresh.calendar], ["inbox", "other"])
        self.assertEqual([row.task_id for row in fresh.calendar], ["id", "id"])
        self.assertTrue(fresh.calendar[0].completed)
        self.assertEqual(fresh.calendar[0].scheduled, date(2026, 11, 15))
        self.assertEqual(fresh.calendar[1].scheduled.isoformat(), "2026-11-15T00:45:00+01:00")

    def test_calendar_keeps_date_only_and_orders_the_repeated_dst_hour(self):
        self.source = AccountTasks("account", (
            TaskList("inbox", "Saved", (
                Task("late", "Second local hour", datetime(2026, 10, 25, 1, 15, tzinfo=UTC)),
                Task("day", "Date only", date(2026, 10, 25)),
                Task("early", "First local hour", datetime(2026, 10, 25, 0, 45, tzinfo=UTC)),
            )),
        ))
        calendar = self.refresh().calendar
        self.assertEqual([row.task_id for row in calendar], ["day", "early", "late"])
        self.assertEqual([row.scheduled.isoformat() for row in calendar],
                         ["2026-10-25", "2026-10-25T02:45:00+02:00", "2026-10-25T02:15:00+01:00"])
        self.views.select(ViewSelection("account", "inbox", ZONE, date(2026, 3, 29), date(2026, 3, 29)))
        self.source = AccountTasks("account", (TaskList("inbox", "Saved", (
            Task("after", "After jump", datetime(2026, 3, 29, 1, 1, tzinfo=UTC)),
            Task("before", "Before jump", datetime(2026, 3, 29, 0, 59, tzinfo=UTC)),
        )),))
        self.assertEqual([row.scheduled.isoformat() for row in self.refresh().calendar],
                         ["2026-03-29T01:59:00+01:00", "2026-03-29T03:01:00+02:00"])

    def test_empty_selected_list_is_distinct_from_absent_list(self):
        self.source = AccountTasks("account", (TaskList("inbox", "Saved", ()),))
        result = self.refresh()
        self.assertEqual((result.status, result.saved_items, result.calendar), ("online", (), ()))
        self.assertEqual(result.saved_list_title, "Saved")
        self.source = AccountTasks("account", (TaskList("other", "Other", (Task("t", "Today", self.now),)),))
        result = self.refresh()
        self.assertIsNone(result.saved_items)
        self.assertEqual([row.task_id for row in result.calendar], ["t"])

    def test_completed_items_do_not_consume_the_saved_items_limit(self):
        self.views = TaskViews(limit=2, clock=lambda: self.now)
        self.views.select(self.selection)
        self.source = AccountTasks("account", (TaskList("inbox", "Saved", (
            Task("closed-a", "Closed A", completed=True),
            Task("closed-b", "Closed B", completed=True),
            Task("first", "First open"),
            Task("closed-c", "Closed C", completed=True),
            Task("second", "Second open"),
        )),))
        result = self.refresh()
        self.assertEqual([row.task_id for row in result.saved_items], ["first", "second"])
        self.assertFalse(result.saved_items_truncated)

    def test_caps_both_views_while_finding_the_earliest_calendar_tasks(self):
        self.views = TaskViews(limit=2, clock=lambda: self.now)
        self.views.select(self.selection)
        self.source = AccountTasks("account", iter((
            TaskList("inbox", "Saved", iter((
                Task("future", "Later", date(2026, 11, 30)),
                Task("none", "Unscheduled"),
                Task("mid", "Earlier", date(2026, 11, 3)),
            ))),
            TaskList("other", "Other", iter((Task("earliest", "Earliest", date(2026, 10, 1)),))),
        )))
        result = self.refresh()
        self.assertEqual([row.task_id for row in result.saved_items], ["future", "none"])
        self.assertEqual([row.task_id for row in result.calendar], ["earliest", "mid"])
        self.assertTrue(result.saved_items_truncated)
        self.assertTrue(result.calendar_truncated)

    def test_repeat_offline_failures_keep_original_update_time_and_expire_at_24_hours(self):
        updated = self.refresh().updated_at

        def offline(_):
            raise ProviderOffline

        self.now += timedelta(hours=23, minutes=59)
        result = self.views.refresh(offline)
        self.assertEqual(result.status, "offline")
        self.assertEqual(result.updated_at, updated)
        self.assertEqual([row.task_id for row in result.saved_items], ["none", "outside"])
        self.now += timedelta(minutes=1)
        self.assert_unavailable(self.views.refresh(offline))
        self.now -= timedelta(minutes=1)
        self.assert_unavailable(self.views.offline())

    def test_offline_age_uses_utc_across_dst_and_rejects_a_future_cache(self):
        self.now = datetime(2026, 10, 24, 12, tzinfo=UTC).astimezone(ZONE)
        self.refresh()
        self.now = datetime(2026, 10, 25, 11, 30, tzinfo=UTC).astimezone(ZONE)
        self.assertEqual(self.views.offline().status, "offline")
        self.now = datetime(2026, 10, 25, 12, tzinfo=UTC).astimezone(ZONE)
        self.assert_unavailable(self.views.offline())
        self.refresh()
        self.now -= timedelta(minutes=1)
        self.assert_unavailable(self.views.offline())

    def test_partial_refresh_keeps_only_the_last_complete_snapshot(self):
        original = self.refresh()
        self.now += timedelta(minutes=1)

        def interrupted_rows():
            yield Task("new", "Partial provider result")
            raise ProviderOffline

        self.source = AccountTasks("account", (TaskList("inbox", "Saved", interrupted_rows()),))
        result = self.refresh()
        self.assertEqual(result.status, "offline")
        self.assertEqual(result.updated_at, original.updated_at)
        self.assertEqual(result.saved_items, original.saved_items)
        self.assertEqual(result.calendar, original.calendar)

    def test_revocation_clears_both_views_until_account_is_bound_again(self):
        self.refresh()

        def revoked(_):
            raise AccessRevoked

        self.assert_unavailable(self.views.refresh(revoked))
        self.assert_unavailable(self.views.offline())
        self.assert_unavailable(self.views.refresh(lambda _: self.fail("Read after revocation")))
        self.views.select(self.selection)
        self.assertEqual(self.refresh().status, "online")

    def test_switching_account_or_list_drops_views_and_read_checks_the_account(self):
        self.refresh()
        self.views.select(ViewSelection("different", "inbox", ZONE, date(2026, 10, 1), date(2026, 11, 30)))
        self.assert_unavailable(self.views.offline())
        self.assert_unavailable(self.views.refresh(lambda _: self.source))
        self.views.select(self.selection)
        self.assert_unavailable(self.views.offline())
        self.refresh()
        self.views.select(ViewSelection("account", "other", ZONE, date(2026, 10, 1), date(2026, 11, 30)))
        self.assert_unavailable(self.views.offline())

    def test_disconnect_while_reading_blocks_a_late_result_even_after_same_account_reconnects(self):
        self.refresh()

        def disconnect(_):
            self.views.select(None)
            self.views.select(self.selection)
            return self.source

        self.assert_unavailable(self.views.refresh(disconnect))
        self.assert_unavailable(self.views.offline())
        self.assertEqual(self.refresh().status, "online")

    def test_naive_provider_timestamp_is_rejected_without_publishing_it(self):
        original = self.refresh()
        self.source = AccountTasks("account", (
            TaskList("inbox", "Saved", (Task("bad", "Missing offset", datetime(2026, 11, 27, 3)),)),
        ))
        with self.assertRaisesRegex(ValueError, "offset"):
            self.refresh()
        self.assertEqual(self.views.offline().saved_items, original.saved_items)

    def assert_unavailable(self, result):
        self.assertEqual(result.status, "unavailable")
        self.assertIsNone(result.saved_items)
        self.assertIsNone(result.calendar)
        self.assertIsNone(result.updated_at)


if __name__ == "__main__":
    unittest.main()
