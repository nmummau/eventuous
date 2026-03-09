// Copyright (C) Eventuous HQ OÜ. All rights reserved
// Licensed under the Apache License, Version 2.0.

using System.Runtime.InteropServices;

namespace Eventuous.Producers;

[StructLayout(LayoutKind.Auto)]
public record struct ProduceRequest(StreamName Stream, IEnumerable<ProducedMessage> Messages);

[StructLayout(LayoutKind.Auto)]
public record struct ProduceRequest<TProduceOptions>(StreamName Stream, IEnumerable<ProducedMessage> Messages, TProduceOptions? Options)
    where TProduceOptions : class;
