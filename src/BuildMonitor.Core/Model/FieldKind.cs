/// <summary>
/// The widgets a form page is made of. Each head realises them with its toolkit's own controls,
/// which is what gives the forms the OS's keyboard handling and screen reader support. Keep in
/// sync with BmFieldKind in bm.h.
/// </summary>
enum FieldKind
{
    Label = 0,
    Checkbox = 1,
    Text = 2,
    Password = 3,
    Number = 4,
    Select = 5,
    Button = 6,
    Link = 7,
    // A read-only line with a remove affordance, for lists such as filters and connections.
    ListRow = 8
}
