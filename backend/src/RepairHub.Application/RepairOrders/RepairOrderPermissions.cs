using RepairHub.Application.Common;
using RepairHub.Domain.RepairOrders;
using RepairHub.Domain.Users;

namespace RepairHub.Application.RepairOrders;

/// <summary>
/// Who may do what with a repair order. Every role sees all orders of its own workshop; changes are limited:
/// <list type="bullet">
/// <item>Front office (Owner, Admin, Receptionist): create, intake, prices and expected date, snapshot corrections, assignment.</item>
/// <item>Diagnostics: Owner, Admin, or the assigned Technician.</item>
/// <item>Status changes: <see cref="RepairOrderWorkflow"/>.</item>
/// <item>Take an unassigned order: any active Technician, Admin or Owner (never a Receptionist).</item>
/// <item>Notes: everyone, also on closed orders.</item>
/// </list>
/// Closed orders (Issued, Cancelled) accept notes only.
/// </summary>
public static class RepairOrderPermissions
{
    public static bool CanCreate(Actor actor) => actor.IsFrontOffice;

    public static bool CanEditIntake(Actor actor, RepairOrder order) => actor.IsFrontOffice && !order.IsClosed;

    /// <summary>
    /// Prices and expected completion date are agreed with the customer at the desk, so technicians do not change
    /// them; a technician who finds the estimate wrong moves the order to AwaitingApproval and adds a note.
    /// </summary>
    public static bool CanEditTerms(Actor actor, RepairOrder order) => actor.IsFrontOffice && !order.IsClosed;

    public static bool CanCorrectSnapshot(Actor actor, RepairOrder order) => actor.IsFrontOffice && !order.IsClosed;

    public static bool CanAssign(Actor actor, RepairOrder order) => actor.IsFrontOffice && !order.IsClosed;

    public static bool CanEditDiagnostics(Actor actor, RepairOrder order) =>
        !order.IsClosed && (actor.IsManager || IsAssignedTechnician(actor, order));

    public static bool CanTake(Actor actor, RepairOrder order) =>
        !order.IsClosed && order.AssignedTechnicianId is null && WorkshopAccess.IsAssignableRole(actor.Role);

    public static bool CanAddNote(Actor actor, RepairOrder order) => true;

    public static bool CanChangeStatus(Actor actor, RepairOrder order, StatusTransition transition) =>
        RepairOrderWorkflow.IsAllowedFor(transition, actor.Role, order.AssignedTechnicianId == actor.UserId);

    private static bool IsAssignedTechnician(Actor actor, RepairOrder order) =>
        actor.Role == UserRole.Technician && order.AssignedTechnicianId == actor.UserId;

    public static RepairOrderPermissionsDto For(Actor actor, RepairOrder order) =>
        new(
            EditIntake: CanEditIntake(actor, order),
            EditDiagnostics: CanEditDiagnostics(actor, order),
            EditTerms: CanEditTerms(actor, order),
            CorrectSnapshot: CanCorrectSnapshot(actor, order),
            Assign: CanAssign(actor, order),
            Take: CanTake(actor, order),
            AddNote: CanAddNote(actor, order));
}
