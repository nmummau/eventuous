CREATE OR ALTER PROCEDURE __schema__.read_stream_backwards
    @stream_name NVARCHAR(850),
    @from_position INT,
    @count INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE
        @current_version INT,
        @stream_id INT;

    SELECT
        @current_version = [Version],
        @stream_id = StreamId
    FROM __schema__.Streams
    WHERE StreamName = @stream_name;

    IF @stream_id IS NULL
    BEGIN
        ;THROW 50001, 'StreamNotFound', 1;
    END;

    IF @current_version < @from_position + @count
    BEGIN
        RETURN;
    END;

    SELECT TOP (@count)
        MessageId,
        MessageType,
        StreamPosition,
        GlobalPosition,
        JsonData,
        JsonMetadata,
        Created
    FROM __schema__.[Messages]
    WHERE StreamId = @stream_id
    AND StreamPosition <= @from_position
    ORDER BY StreamPosition DESC;
END;