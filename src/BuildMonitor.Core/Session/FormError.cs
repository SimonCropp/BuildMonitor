/// <summary>
/// What is wrong with a form, and the id of the field it is about, which the page draws it under.
/// <see cref="Field"/> is null for an error about the whole form, such as a test the service
/// refused, which goes at the foot of the page. Every error used to go there, so "Enter the API
/// token" sat below the documentation links rather than at the box it asked to be filled in.
/// </summary>
record FormError(string Text, string? Field = null);
