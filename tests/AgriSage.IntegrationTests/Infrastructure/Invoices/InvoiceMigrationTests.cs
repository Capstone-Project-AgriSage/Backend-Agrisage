using AgriSage.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace AgriSage.IntegrationTests.Infrastructure.Invoices;

public class InvoiceMigrationTests
{
    [Fact]
    public void Invoice_extension_is_additive_scoped_and_preserves_business_references()
    {
        var operations = new SalesInvoices().UpOperations;
        Assert.Equal(new[] { "invoice_emails", "sales_invoices" }, operations.OfType<CreateTableOperation>().Select(x => x.Name).Order());
        Assert.DoesNotContain(operations, x => x is DropTableOperation or DropColumnOperation or AlterColumnOperation or DeleteDataOperation);
        Assert.All(operations.OfType<CreateTableOperation>().SelectMany(x => x.ForeignKeys), fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        Assert.Contains(operations.OfType<CreateIndexOperation>(), index => index.Name == "ux_sales_invoices_order_id" && index.IsUnique);
        Assert.Contains(operations.OfType<CreateIndexOperation>(), index => index.Name == "ux_invoice_emails_invoice_id_request_id" && index.IsUnique);
        var invoice = operations.OfType<CreateTableOperation>().Single(x => x.Name == "sales_invoices");
        Assert.Equal("numeric(18,2)", invoice.Columns.Single(x => x.Name == "total_amount").ColumnType);
        Assert.Equal("bytea", invoice.Columns.Single(x => x.Name == "pdf_content").ColumnType);
        Assert.Equal("jsonb", invoice.Columns.Single(x => x.Name == "snapshot_json").ColumnType);
        Assert.Contains(operations.OfType<SqlOperation>(), x => x.Sql.Contains("INSERT INTO role_permissions") && x.Sql.Contains("SALES_STAFF"));
    }
}
