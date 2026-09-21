/// <summary>
/// Which icon a notification pops with, where the desktop draws one. A build that failed was once
/// the only thing the tray announced, so Windows drew every balloon with the error icon, and a
/// triage prompt ready to paste would have looked like something had gone wrong.
/// </summary>
enum NotificationKind
{
    Error,
    Info
}
