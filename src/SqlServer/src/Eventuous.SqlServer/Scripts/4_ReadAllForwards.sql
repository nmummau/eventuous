CREATE OR ALTER PROCEDURE __schema__.read_all_forwards
    @from_position BIGINT,
    @count INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    SELECT TOP (@count)
        m.MessageId,
        m.MessageType,
        m.StreamPosition,
        m.GlobalPosition,
        m.JsonData,
        m.JsonMetadata,
        m.Created,
        s.StreamName
    FROM __schema__.Messages m
    JOIN __schema__.Streams s ON m.StreamId = s.StreamId
    WHERE m.GlobalPosition >= @from_position
    ORDER BY m.GlobalPosition;
END;