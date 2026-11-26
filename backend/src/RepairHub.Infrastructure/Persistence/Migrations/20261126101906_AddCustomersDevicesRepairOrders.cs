using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RepairHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomersDevicesRepairOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_users_workshop_id_id",
                table: "users",
                columns: new[] { "workshop_id", "id" });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workshop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    phone_key = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    additional_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    additional_phone_key = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.UniqueConstraint("ak_customers_workshop_id_id", x => new { x.workshop_id, x.id });
                    table.ForeignKey(
                        name: "fk_customers_workshops_workshop_id",
                        column: x => x.workshop_id,
                        principalTable: "workshops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "repair_order_counters",
                columns: table => new
                {
                    workshop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_value = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repair_order_counters", x => new { x.workshop_id, x.year });
                    table.ForeignKey(
                        name: "fk_repair_order_counters_workshops_workshop_id",
                        column: x => x.workshop_id,
                        principalTable: "workshops",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workshop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    model = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    imei = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => x.id);
                    table.UniqueConstraint("ak_devices_workshop_id_id", x => new { x.workshop_id, x.id });
                    table.CheckConstraint("ck_devices_type", "type IN ('Laptop', 'Desktop', 'AllInOne', 'Printer', 'Smartphone', 'Tablet', 'Monitor', 'Other')");
                    table.ForeignKey(
                        name: "fk_devices_customers_workshop_id_customer_id",
                        columns: x => new { x.workshop_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: new[] { "workshop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "repair_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workshop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    number_year = table.Column<int>(type: "integer", nullable: false),
                    number_seq = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    device_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    device_brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    device_model = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    device_serial_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    device_imei = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    problem = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    condition = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    accessories = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    diagnostics = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    estimated_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    final_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    assigned_technician_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expected_completion_date = table.Column<DateOnly>(type: "date", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repair_orders", x => x.id);
                    table.UniqueConstraint("ak_repair_orders_workshop_id_id", x => new { x.workshop_id, x.id });
                    table.CheckConstraint("ck_repair_orders_device_type", "device_type IN ('Laptop', 'Desktop', 'AllInOne', 'Printer', 'Smartphone', 'Tablet', 'Monitor', 'Other')");
                    table.CheckConstraint("ck_repair_orders_outcome", "outcome IS NULL OR outcome IN ('Repaired', 'NotRepaired', 'Declined')");
                    table.CheckConstraint("ck_repair_orders_prices", "(estimated_price IS NULL OR estimated_price >= 0) AND (final_price IS NULL OR final_price >= 0)");
                    table.CheckConstraint("ck_repair_orders_status", "status IN ('Received', 'Diagnostics', 'AwaitingApproval', 'WaitingForParts', 'InRepair', 'ReadyForPickup', 'Issued', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_repair_orders_customers_workshop_id_customer_id",
                        columns: x => new { x.workshop_id, x.customer_id },
                        principalTable: "customers",
                        principalColumns: new[] { "workshop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_repair_orders_devices_workshop_id_device_id",
                        columns: x => new { x.workshop_id, x.device_id },
                        principalTable: "devices",
                        principalColumns: new[] { "workshop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_repair_orders_users_workshop_id_assigned_technician_id",
                        columns: x => new { x.workshop_id, x.assigned_technician_id },
                        principalTable: "users",
                        principalColumns: new[] { "workshop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_repair_orders_users_workshop_id_created_by_id",
                        columns: x => new { x.workshop_id, x.created_by_id },
                        principalTable: "users",
                        principalColumns: new[] { "workshop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_repair_orders_users_workshop_id_updated_by_id",
                        columns: x => new { x.workshop_id, x.updated_by_id },
                        principalTable: "users",
                        principalColumns: new[] { "workshop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "repair_order_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workshop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repair_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    old_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    new_value = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_repair_order_events", x => x.id);
                    table.CheckConstraint("ck_repair_order_events_type", "type IN ('Created', 'StatusChanged', 'OutcomeRecorded', 'TechnicianAssigned', 'DiagnosticsUpdated', 'ProblemUpdated', 'ConditionUpdated', 'AccessoriesUpdated', 'EstimateChanged', 'FinalPriceChanged', 'ExpectedDateChanged', 'CustomerInfoCorrected', 'DeviceInfoCorrected', 'NoteAdded')");
                    table.ForeignKey(
                        name: "fk_repair_order_events_repair_orders_workshop_id_repair_order_",
                        columns: x => new { x.workshop_id, x.repair_order_id },
                        principalTable: "repair_orders",
                        principalColumns: new[] { "workshop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_repair_order_events_users_workshop_id_actor_user_id",
                        columns: x => new { x.workshop_id, x.actor_user_id },
                        principalTable: "users",
                        principalColumns: new[] { "workshop_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customers_workshop_id_additional_phone_key",
                table: "customers",
                columns: new[] { "workshop_id", "additional_phone_key" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_workshop_id_full_name",
                table: "customers",
                columns: new[] { "workshop_id", "full_name" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_workshop_id_phone_key",
                table: "customers",
                columns: new[] { "workshop_id", "phone_key" });

            migrationBuilder.CreateIndex(
                name: "ix_devices_workshop_id_customer_id",
                table: "devices",
                columns: new[] { "workshop_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_devices_workshop_id_imei",
                table: "devices",
                columns: new[] { "workshop_id", "imei" });

            migrationBuilder.CreateIndex(
                name: "ix_devices_workshop_id_serial_number",
                table: "devices",
                columns: new[] { "workshop_id", "serial_number" });

            migrationBuilder.CreateIndex(
                name: "ix_repair_order_events_repair_order_id_occurred_at",
                table: "repair_order_events",
                columns: new[] { "repair_order_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_repair_order_events_workshop_id_actor_user_id",
                table: "repair_order_events",
                columns: new[] { "workshop_id", "actor_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_repair_order_events_workshop_id_repair_order_id",
                table: "repair_order_events",
                columns: new[] { "workshop_id", "repair_order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_repair_orders_workshop_id_assigned_technician_id_status",
                table: "repair_orders",
                columns: new[] { "workshop_id", "assigned_technician_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_repair_orders_workshop_id_created_by_id",
                table: "repair_orders",
                columns: new[] { "workshop_id", "created_by_id" });

            migrationBuilder.CreateIndex(
                name: "ix_repair_orders_workshop_id_customer_id",
                table: "repair_orders",
                columns: new[] { "workshop_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_repair_orders_workshop_id_device_id",
                table: "repair_orders",
                columns: new[] { "workshop_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "ix_repair_orders_workshop_id_received_at",
                table: "repair_orders",
                columns: new[] { "workshop_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_repair_orders_workshop_id_status_received_at",
                table: "repair_orders",
                columns: new[] { "workshop_id", "status", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_repair_orders_workshop_id_updated_by_id",
                table: "repair_orders",
                columns: new[] { "workshop_id", "updated_by_id" });

            migrationBuilder.CreateIndex(
                name: "ux_repair_orders_workshop_id_number",
                table: "repair_orders",
                columns: new[] { "workshop_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "repair_order_counters");

            migrationBuilder.DropTable(
                name: "repair_order_events");

            migrationBuilder.DropTable(
                name: "repair_orders");

            migrationBuilder.DropTable(
                name: "devices");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_users_workshop_id_id",
                table: "users");
        }
    }
}
