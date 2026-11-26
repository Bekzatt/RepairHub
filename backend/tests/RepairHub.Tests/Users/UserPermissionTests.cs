using System.Net;
using RepairHub.Application.Users;
using RepairHub.Domain.Users;
using RepairHub.Tests.Infrastructure;

namespace RepairHub.Tests.Users;

[Collection(PostgresCollection.Name)]
public sealed class UserPermissionTests(PostgresFixture db)
{
    private IntegrationApiFactory Api => db.Api;

    private static object NewUser(string role) =>
        new { fullName = "New " + role, email = TestApi.UniqueEmail(), role, password = TestApi.Password };

    // ---- Role-based access to endpoints -------------------------------------------------------

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Receptionist)]
    public async Task StaffRoles_CannotAccessUserManagement(UserRole role)
    {
        var workshop = await Api.CreateWorkshopAsync();
        var staff = await Api.AddUserAsync(workshop.Owner, role);

        Assert.Equal(HttpStatusCode.Forbidden, (await Api.GetAsAsync(staff, "/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.GetAsAsync(staff, $"/api/users/{staff.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.PostAsAsync(staff, "/api/users", NewUser("Technician"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Api.PostAsAsync(staff, $"/api/users/{workshop.Owner.UserId}/deactivate")).StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Receptionist)]
    [InlineData(UserRole.Admin)]
    public async Task OnlyOwner_CanUpdateWorkshopSettings_ButEveryoneCanReadThem(UserRole role)
    {
        var workshop = await Api.CreateWorkshopAsync();
        var user = await Api.AddUserAsync(workshop.Owner, role);
        var settings = new { name = "Renamed", currency = "KZT", timeZone = "Asia/Almaty" };

        Assert.Equal(HttpStatusCode.OK, (await Api.GetAsAsync(user, "/api/workshop")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Api.PutAsAsync(user, "/api/workshop", settings)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Api.PutAsAsync(workshop.Owner, "/api/workshop", settings)).StatusCode);
    }

    [Fact]
    public async Task UpdateWorkshop_ValidatesCurrencyAndTimeZone()
    {
        var workshop = await Api.CreateWorkshopAsync();

        var response = await Api.PutAsAsync(workshop.Owner, "/api/workshop",
            new { name = "X", currency = "TENGE", timeZone = "Mars/Olympus" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"currency\"", body);
        Assert.Contains("\"timeZone\"", body);
    }

    // ---- Admin privilege escalation ------------------------------------------------------------

    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    public async Task Admin_CannotCreateOwnersOrAdmins(string role)
    {
        var workshop = await Api.CreateWorkshopAsync();
        var admin = await Api.AddUserAsync(workshop.Owner, UserRole.Admin);

        var response = await Api.PostAsAsync(admin, "/api/users", NewUser(role));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("role_not_allowed", await response.ReadErrorCodeAsync());
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    public async Task Admin_CannotPromoteStaffToOwnerOrAdmin(string role)
    {
        var workshop = await Api.CreateWorkshopAsync();
        var admin = await Api.AddUserAsync(workshop.Owner, UserRole.Admin);
        var technician = await Api.AddUserAsync(workshop.Owner, UserRole.Technician);

        var response = await Api.PutAsAsync(admin, $"/api/users/{technician.UserId}", new { fullName = "T", role });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var unchanged = await (await Api.GetAsAsync(workshop.Owner, $"/api/users/{technician.UserId}")).ReadAsync<UserDto>();
        Assert.Equal(UserRole.Technician, unchanged!.Role);
    }

    [Fact]
    public async Task Admin_CannotPromoteThemselves()
    {
        var workshop = await Api.CreateWorkshopAsync();
        var admin = await Api.AddUserAsync(workshop.Owner, UserRole.Admin);

        var response = await Api.PutAsAsync(admin, $"/api/users/{admin.UserId}", new { fullName = "Me", role = "Owner" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("self_modification", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Admin_CannotManageOwnersOrOtherAdmins()
    {
        var workshop = await Api.CreateWorkshopAsync();
        var admin = await Api.AddUserAsync(workshop.Owner, UserRole.Admin);
        var otherAdmin = await Api.AddUserAsync(workshop.Owner, UserRole.Admin);
        var owner = workshop.Owner;

        foreach (var target in new[] { owner, otherAdmin })
        {
            var role = target == owner ? "Owner" : "Admin";
            Assert.Equal(HttpStatusCode.Forbidden,
                (await Api.PutAsAsync(admin, $"/api/users/{target.UserId}", new { fullName = "X", role })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await Api.PutAsAsync(admin, $"/api/users/{target.UserId}", new { fullName = "X", role = "Technician" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await Api.PostAsAsync(admin, $"/api/users/{target.UserId}/deactivate")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await Api.PostAsAsync(admin, $"/api/users/{target.UserId}/reset-password",
                    new { newPassword = "taken-over-password" })).StatusCode);
        }

        // Owner's password was not changed by the attempts above.
        await Api.LoginAsync(owner.Auth.User.Email, TestApi.Password);
    }

    [Fact]
    public async Task Admin_CanManageTechniciansAndReceptionists()
    {
        var workshop = await Api.CreateWorkshopAsync();
        var admin = await Api.AddUserAsync(workshop.Owner, UserRole.Admin);

        var created = await Api.PostAsAsync(admin, "/api/users", NewUser("Receptionist"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var receptionist = await created.ReadAsync<UserDto>();

        var update = await Api.PutAsAsync(admin, $"/api/users/{receptionist!.Id}",
            new { fullName = "Front desk", phone = "+7 700 000 00 00", role = "Technician" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(UserRole.Technician, (await update.ReadAsync<UserDto>())!.Role);

        Assert.Equal(HttpStatusCode.NoContent,
            (await Api.PostAsAsync(admin, $"/api/users/{receptionist.Id}/reset-password",
                new { newPassword = "temporary-password-1" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Api.PostAsAsync(admin, $"/api/users/{receptionist.Id}/deactivate")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Api.PostAsAsync(admin, $"/api/users/{receptionist.Id}/activate")).StatusCode);
    }

    // ---- Self-protection and Owner rules -------------------------------------------------------

    [Fact]
    public async Task Users_CannotDeactivateThemselvesOrChangeOwnRole()
    {
        var workshop = await Api.CreateWorkshopAsync();
        var owner = workshop.Owner;

        var deactivate = await Api.PostAsAsync(owner, $"/api/users/{owner.UserId}/deactivate");
        var demote = await Api.PutAsAsync(owner, $"/api/users/{owner.UserId}", new { fullName = "Me", role = "Admin" });
        var resetOwn = await Api.PostAsAsync(owner, $"/api/users/{owner.UserId}/reset-password",
            new { newPassword = "another-password-1" });

        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, resetOwn.StatusCode);
        Assert.Equal("self_modification", await deactivate.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Users_CanEditOwnNameAndPhone()
    {
        var workshop = await Api.CreateWorkshopAsync();

        var response = await Api.PutAsAsync(workshop.Owner, $"/api/users/{workshop.Owner.UserId}",
            new { fullName = "Renamed Owner", phone = "+7 701 111 22 33", role = "Owner" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Renamed Owner", (await response.ReadAsync<UserDto>())!.FullName);
    }

    [Fact]
    public async Task Owner_CanManageOtherOwners_WhileAnotherActiveOwnerRemains()
    {
        var workshop = await Api.CreateWorkshopAsync();
        var secondOwner = await Api.AddUserAsync(workshop.Owner, UserRole.Owner);

        var demote = await Api.PutAsAsync(workshop.Owner, $"/api/users/{secondOwner.UserId}",
            new { fullName = "Former owner", role = "Admin" });

        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WithDuplicateEmail_Returns409_EvenAcrossWorkshops()
    {
        var a = await Api.CreateWorkshopAsync();
        var b = await Api.CreateWorkshopAsync();

        var response = await Api.PostAsAsync(a.Owner, "/api/users", new
        {
            fullName = "Duplicate",
            email = b.Owner.Auth.User.Email.ToUpperInvariant(),
            role = "Technician",
            password = TestApi.Password,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("email_taken", await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task CreateUser_ValidatesInput()
    {
        var workshop = await Api.CreateWorkshopAsync();

        var weakPassword = await Api.PostAsAsync(workshop.Owner, "/api/users",
            new { fullName = "X", email = TestApi.UniqueEmail(), role = "Technician", password = "short" });
        var missingRole = await Api.PostAsAsync(workshop.Owner, "/api/users",
            new { fullName = "X", email = TestApi.UniqueEmail(), password = TestApi.Password });
        var badRole = await Api.PostAsAsync(workshop.Owner, "/api/users",
            new { fullName = "X", email = TestApi.UniqueEmail(), role = "SuperUser", password = TestApi.Password });

        Assert.Equal(HttpStatusCode.BadRequest, weakPassword.StatusCode);
        Assert.Contains("\"password\"", await weakPassword.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, missingRole.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badRole.StatusCode);
    }
}
