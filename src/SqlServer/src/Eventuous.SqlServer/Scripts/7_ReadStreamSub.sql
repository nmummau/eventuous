CREATE OR ALTER PROCEDURE __schema__.read_stream_sub
    @stream_id INT,
    @stream_name NVARCHAR(850),
    @from_position BIGINT,
    @count INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT TOP (@count)
        MessageId
        MessageType,
        StreamPosition,
        GlobalPosition,
        JsonData,
        JsonMetadata,
        Created,
        @stream_name StreamName
    FROM __schema__.[Messages]
    WHERE StreamId = @stream_id
    AND StreamPosition >= @from_position
    ORDER BY GlobalPosition;
END;