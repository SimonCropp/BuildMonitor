/// <summary>
/// What a socket message can do to the window. Queued for the render loop, because every head
/// is single threaded and the listener is not on that thread.
/// </summary>
enum WindowCommand
{
    Show,
    Hide,
    Focus,
    Close
}
