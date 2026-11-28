#pragma once

#if PALA_STAGE3
#include "archive_month_job.h"

struct ArchiveMonthConfirmation {
  archive_month::Month month;
  archive_month::Inventory inventory;
  std::string token;
};

void mountArchiveMonthDeletion();
bool archiveMonthDeletionActive();
bool archiveMonthDeletionFailed();
void maintainArchiveMonthDeletion();
void cancelArchiveMonthConfirmation();
std::optional<ArchiveMonthConfirmation> prepareArchiveMonthDeletion(archive_month::Month month);
archive_month::Stage confirmArchiveMonthDeletion(archive_month::Month month, std::string_view token);
#endif
