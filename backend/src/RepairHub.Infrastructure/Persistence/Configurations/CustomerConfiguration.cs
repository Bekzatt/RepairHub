using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RepairHub.Domain.Common;
using RepairHub.Domain.Customers;
using RepairHub.Domain.Workshops;

namespace RepairHub.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        // (workshop_id, id) is the target of same-workshop foreign keys from devices and repair orders.
        builder.HasAlternateKey(c => new { c.WorkshopId, c.Id });

        builder.Property(c => c.FullName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Phone).HasMaxLength(32).IsRequired();
        builder.Property(c => c.PhoneKey).HasMaxLength(PhoneNumber.KeyLength).IsRequired();
        builder.Property(c => c.AdditionalPhone).HasMaxLength(32);
        builder.Property(c => c.AdditionalPhoneKey).HasMaxLength(PhoneNumber.KeyLength);
        builder.Property(c => c.Email).HasMaxLength(254);
        builder.Property(c => c.Notes).HasMaxLength(2000);

        builder.HasIndex(c => new { c.WorkshopId, c.PhoneKey });
        builder.HasIndex(c => new { c.WorkshopId, c.AdditionalPhoneKey });
        builder.HasIndex(c => new { c.WorkshopId, c.FullName });

        builder.HasOne<Workshop>().WithMany().HasForeignKey(c => c.WorkshopId).OnDelete(DeleteBehavior.Restrict);
    }
}
