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
    // A read-only line whose only click target is a remove cross, for a list whose rows are
    // deleted in place, such as the filters.
    ListRow = 8,
    // A read-only line that is itself the click target, for a list whose rows open an editor,
    // such as the connections. Distinct from ListRow because a field carries one command, so the
    // affordance has to say which of the two it is: a cross on a row that opens an editor reads
    // as a delete, which is the one thing it does not do.
    EditRow = 9
}
