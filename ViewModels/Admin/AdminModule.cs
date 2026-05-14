namespace PosSystem.ViewModels.Admin;

// Identifies the active admin module. The shell uses this to switch the
// page-host content and highlight the matching sidebar item.
public enum AdminModule
{
    Dashboard,
    Sales,
    Returns,
    Products,
    Inventory,
    Employees,
    Customers,
    Statistics,
    Settings,
}
