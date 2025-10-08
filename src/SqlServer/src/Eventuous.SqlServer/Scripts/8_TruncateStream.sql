CREATE OR ALTER PROCEDURE __schema__.truncate_stream
    @stream_name NVARCHAR(850),
    @expected_version INT,
    @position INT
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

    IF @current_version < @position
    BEGIN
        RETURN;
    END;

    IF @expected_version != -2 AND @expected_version != @current_version
    BEGIN
        ;THROW 50000, 'WrongExpectedVersion %, current version %', 1;
    END;

    DELETE m
    FROM __schema__.Messages m
    WHERE m.StreamId = @stream_id
    AND m.StreamPosition < @position;
END;