#pragma once

#if PALA_WORKSPACE_CALENDAR
void refreshWorkspaceCalendar();
int workspaceCalendarPages();
void showWorkspaceCalendar(int page);
#if PALA_STAGE3
int workspaceSavedItemsPages();
void showWorkspaceSavedItems(int page);
#endif
#endif
