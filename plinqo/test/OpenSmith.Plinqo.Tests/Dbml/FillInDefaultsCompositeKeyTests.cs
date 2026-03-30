using LinqToSqlShared.DbmlObjectModel;

namespace OpenSmith.Plinqo.Tests.DbmlModel;

public class FillInDefaultsCompositeKeyTests
{
    /// <summary>
    /// Reproduces a bug where FillInDefaults fills in OtherKey using PK column-definition
    /// order instead of preserving the FK's ThisKey correspondence order.
    ///
    /// Given: TmCalendar has FK (ContactId, CalendarEventId) -> TmCalendarEvent (ContactId, CalendarEventId)
    /// But TmCalendarEvent's PK columns are defined as (CalendarEventId, ContactId) in column order.
    /// When OtherKey is null (omitted from DBML as a default), FillInDefaults should produce
    /// OtherKey="ContactId,CalendarEventId" to match ThisKey order, not "CalendarEventId,ContactId".
    /// </summary>
    [Fact]
    public void FillInDefaults_CompositeFK_OtherKeyMatchesThisKeyOrder()
    {
        // Arrange: build a minimal Database with two types and a composite FK
        var db = new Database { Name = "TestDb" };
        db.Connection = new Connection("System.Data.SqlClient") { ConnectionString = "test" };

        // TmCalendarEvent: PK columns defined in (CalendarEventId, ContactId) order
        var calendarEventType = new LinqToSqlShared.DbmlObjectModel.Type("TmCalendarEvent");
        calendarEventType.Columns.Add(new Column("System.Int32")
        {
            Name = "CalendarEventId",
            Member = "CalendarEventId",
            IsPrimaryKey = true
        });
        calendarEventType.Columns.Add(new Column("System.Int32")
        {
            Name = "ContactId",
            Member = "ContactId",
            IsPrimaryKey = true
        });

        var calendarEventTable = new Table("dbo.TmCalendarEvent", calendarEventType);
        db.Tables.Add(calendarEventTable);

        // TmCalendar: FK columns reference in (ContactId, CalendarEventId) order
        var calendarType = new LinqToSqlShared.DbmlObjectModel.Type("TmCalendar");
        calendarType.Columns.Add(new Column("System.Int32")
        {
            Name = "ContactId",
            Member = "ContactId",
            IsPrimaryKey = true
        });
        calendarType.Columns.Add(new Column("System.Int32")
        {
            Name = "CalendarEventId",
            Member = "CalendarEventId",
        });

        // FK association: ThisKey in (ContactId, CalendarEventId) order, OtherKey omitted
        var fkAssociation = new Association("FK_TmCalendar_TmCalendarEvent")
        {
            Member = "TmCalendarEvent",
            Type = "TmCalendarEvent",
            IsForeignKey = true,
            ThisKey = "ContactId,CalendarEventId",
            // OtherKey intentionally null - FillInDefaults should fill it in
        };
        calendarType.Associations.Add(fkAssociation);

        // Reverse association on TmCalendarEvent
        var reverseAssociation = new Association("FK_TmCalendar_TmCalendarEvent")
        {
            Member = "TmCalendarList",
            Type = "TmCalendar",
            IsForeignKey = false,
            ThisKey = "ContactId,CalendarEventId",
            OtherKey = "ContactId,CalendarEventId",
        };
        calendarEventType.Associations.Add(reverseAssociation);

        var calendarTable = new Table("dbo.TmCalendar", calendarType);
        db.Tables.Add(calendarTable);

        // Act
        LinqToSqlShared.DbmlObjectModel.Dbml.FillInDefaults(db);

        // Assert: OtherKey should match ThisKey column correspondence, not PK definition order
        // ThisKey = "ContactId,CalendarEventId" so OtherKey must also be "ContactId,CalendarEventId"
        Assert.Equal("ContactId,CalendarEventId", fkAssociation.OtherKey);
    }

    [Fact]
    public void FillInDefaults_CompositeFK_OtherKeyAlreadySet_PreservesOrder()
    {
        // Arrange: same setup but OtherKey is already correctly set
        var db = new Database { Name = "TestDb" };
        db.Connection = new Connection("System.Data.SqlClient") { ConnectionString = "test" };

        var calendarEventType = new LinqToSqlShared.DbmlObjectModel.Type("TmCalendarEvent");
        calendarEventType.Columns.Add(new Column("System.Int32")
        {
            Name = "CalendarEventId",
            Member = "CalendarEventId",
            IsPrimaryKey = true
        });
        calendarEventType.Columns.Add(new Column("System.Int32")
        {
            Name = "ContactId",
            Member = "ContactId",
            IsPrimaryKey = true
        });

        var calendarEventTable = new Table("dbo.TmCalendarEvent", calendarEventType);
        db.Tables.Add(calendarEventTable);

        var calendarType = new LinqToSqlShared.DbmlObjectModel.Type("TmCalendar");
        calendarType.Columns.Add(new Column("System.Int32")
        {
            Name = "ContactId",
            Member = "ContactId",
            IsPrimaryKey = true
        });
        calendarType.Columns.Add(new Column("System.Int32")
        {
            Name = "CalendarEventId",
            Member = "CalendarEventId",
        });

        var fkAssociation = new Association("FK_TmCalendar_TmCalendarEvent")
        {
            Member = "TmCalendarEvent",
            Type = "TmCalendarEvent",
            IsForeignKey = true,
            ThisKey = "ContactId,CalendarEventId",
            OtherKey = "ContactId,CalendarEventId", // already set correctly
        };
        calendarType.Associations.Add(fkAssociation);

        var reverseAssociation = new Association("FK_TmCalendar_TmCalendarEvent")
        {
            Member = "TmCalendarList",
            Type = "TmCalendar",
            IsForeignKey = false,
            ThisKey = "ContactId,CalendarEventId",
            OtherKey = "ContactId,CalendarEventId",
        };
        calendarEventType.Associations.Add(reverseAssociation);

        var calendarTable = new Table("dbo.TmCalendar", calendarType);
        db.Tables.Add(calendarTable);

        // Act
        LinqToSqlShared.DbmlObjectModel.Dbml.FillInDefaults(db);

        // Assert: OtherKey should be unchanged
        Assert.Equal("ContactId,CalendarEventId", fkAssociation.OtherKey);
    }
}
