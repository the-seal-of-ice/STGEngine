using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using STGEngine.Core.DataModel;
using STGEngine.Core.Modifiers;
using STGEngine.Core.Timeline;
// Alias to avoid conflict with YamlDotNet.Core.IEmitter
using IEmitterData = STGEngine.Core.Emitters.IEmitter;
using YamlEmitter = YamlDotNet.Core.IEmitter;

namespace STGEngine.Core.Serialization
{
    /// <summary>
    /// YamlDotNet wrapper with polymorphic type support via TypeRegistry.
    /// Handles IEmitter, IModifier, Vector3, Color, SerializableCurve.
    /// </summary>
    public static class YamlSerializer
    {
        private static ISerializer _serializer;
        private static IDeserializer _deserializer;

        public static ISerializer Serializer
        {
            get
            {
                if (_serializer == null) Build();
                return _serializer;
            }
        }

        public static IDeserializer Deserializer
        {
            get
            {
                if (_deserializer == null) Build();
                return _deserializer;
            }
        }

        private static void Build()
        {
            TypeRegistry.EnsureInitialized();

            var converters = new IYamlTypeConverter[]
            {
                new EmitterTypeConverter(),
                new ModifierTypeConverter(),
                new Vector3TypeConverter(),
                new ColorTypeConverter(),
                new SerializableCurveTypeConverter(),
            };

            var sb = new SerializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull);
            var db = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance);

            foreach (var c in converters)
            {
                sb = sb.WithTypeConverter(c);
                db = db.WithTypeConverter(c);
            }

            _serializer = sb.Build();
            _deserializer = db.Build();
        }

        /// <summary>Force rebuild after TypeRegistry changes.</summary>
        public static void Reset()
        {
            _serializer = null;
            _deserializer = null;
        }

        public static string Serialize(BulletPattern pattern)
        {
            return Serializer.Serialize(pattern);
        }

        public static BulletPattern Deserialize(string yaml)
        {
            return Deserializer.Deserialize<BulletPattern>(yaml);
        }

        public static void SerializeToFile(BulletPattern pattern, string path)
        {
            File.WriteAllText(path, Serialize(pattern));
        }

        public static BulletPattern DeserializeFromFile(string path)
        {
            return Deserialize(File.ReadAllText(path));
        }

        // ─── EnemyType Serialization ───

        public static string SerializeEnemyType(EnemyType enemyType)
        {
            return Serializer.Serialize(enemyType);
        }

        public static EnemyType DeserializeEnemyType(string yaml)
        {
            return Deserializer.Deserialize<EnemyType>(yaml);
        }

        public static void SerializeEnemyTypeToFile(EnemyType enemyType, string path)
        {
            File.WriteAllText(path, SerializeEnemyType(enemyType));
        }

        public static EnemyType DeserializeEnemyTypeFromFile(string path)
        {
            return DeserializeEnemyType(File.ReadAllText(path));
        }

        // ─── Wave Serialization ───

        public static string SerializeWave(Wave wave)
        {
            return Serializer.Serialize(wave);
        }

        public static Wave DeserializeWave(string yaml)
        {
            return Deserializer.Deserialize<Wave>(yaml);
        }

        public static void SerializeWaveToFile(Wave wave, string path)
        {
            File.WriteAllText(path, SerializeWave(wave));
        }

        public static Wave DeserializeWaveFromFile(string path)
        {
            return DeserializeWave(File.ReadAllText(path));
        }

        // ─── SpellCard Serialization ───

        public static string SerializeSpellCard(SpellCard spellCard)
        {
            return Serializer.Serialize(spellCard);
        }

        public static SpellCard DeserializeSpellCard(string yaml)
        {
            return Deserializer.Deserialize<SpellCard>(yaml);
        }

        public static void SerializeSpellCardToFile(SpellCard spellCard, string path)
        {
            File.WriteAllText(path, SerializeSpellCard(spellCard));
        }

        public static SpellCard DeserializeSpellCardFromFile(string path)
        {
            return DeserializeSpellCard(File.ReadAllText(path));
        }

        // ─── Polymorphic TypeConverter for IEmitter ───

        private class EmitterTypeConverter : IYamlTypeConverter
        {
            public bool Accepts(Type type) => typeof(IEmitterData).IsAssignableFrom(type);

            public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
            {
                var props = ReadMapping(parser);

                if (!props.TryGetValue("type", out var tagObj) || tagObj is not string tag)
                    throw new YamlException("Emitter missing 'type' field");

                var concreteType = TypeRegistry.Resolve(tag);
                var instance = Activator.CreateInstance(concreteType);
                ApplyProperties(instance, concreteType, props);
                return instance;
            }

            public void WriteYaml(YamlEmitter emitter, object value, Type type, ObjectSerializer rootSerializer)
            {
                var concreteType = value.GetType();
                var tag = TypeRegistry.GetTag(concreteType);

                emitter.Emit(new MappingStart());
                EmitScalar(emitter, "type");
                EmitScalar(emitter, tag);
                EmitObjectProperties(emitter, value, concreteType);
                emitter.Emit(new MappingEnd());
            }
        }

        // ─── Polymorphic TypeConverter for IModifier ───

        private class ModifierTypeConverter : IYamlTypeConverter
        {
            public bool Accepts(Type type) => typeof(IModifier).IsAssignableFrom(type);

            public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
            {
                var props = ReadMapping(parser);

                if (!props.TryGetValue("type", out var tagObj) || tagObj is not string tag)
                    throw new YamlException("Modifier missing 'type' field");

                var concreteType = TypeRegistry.Resolve(tag);
                var instance = Activator.CreateInstance(concreteType);
                ApplyProperties(instance, concreteType, props);
                return instance;
            }

            public void WriteYaml(YamlEmitter emitter, object value, Type type, ObjectSerializer rootSerializer)
            {
                var concreteType = value.GetType();
                var tag = TypeRegistry.GetTag(concreteType);

                emitter.Emit(new MappingStart());
                EmitScalar(emitter, "type");
                EmitScalar(emitter, tag);
                EmitObjectProperties(emitter, value, concreteType);
                emitter.Emit(new MappingEnd());
            }
        }

        // ─── Vector3 TypeConverter ───

        private class Vector3TypeConverter : IYamlTypeConverter
        {
            public bool Accepts(Type type) => type == typeof(Vector3);

            public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
            {
                parser.Consume<MappingStart>();
                float x = 0, y = 0, z = 0;
                while (!parser.TryConsume<MappingEnd>(out _))
                {
                    var key = parser.Consume<Scalar>().Value;
                    var val = ParseFloat(parser.Consume<Scalar>().Value);
                    switch (key)
                    {
                        case "x": x = val; break;
                        case "y": y = val; break;
                        case "z": z = val; break;
                    }
                }
                return new Vector3(x, y, z);
            }

            public void WriteYaml(YamlEmitter emitter, object value, Type type, ObjectSerializer rootSerializer)
            {
                var v = (Vector3)value;
                emitter.Emit(new MappingStart(default, default, false, MappingStyle.Flow));
                EmitScalar(emitter, "x"); EmitScalar(emitter, Fmt(v.x));
                EmitScalar(emitter, "y"); EmitScalar(emitter, Fmt(v.y));
                EmitScalar(emitter, "z"); EmitScalar(emitter, Fmt(v.z));
                emitter.Emit(new MappingEnd());
            }
        }

        // ─── Color TypeConverter ───

        private class ColorTypeConverter : IYamlTypeConverter
        {
            public bool Accepts(Type type) => type == typeof(Color);

            public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
            {
                parser.Consume<MappingStart>();
                float r = 1, g = 1, b = 1, a = 1;
                while (!parser.TryConsume<MappingEnd>(out _))
                {
                    var key = parser.Consume<Scalar>().Value;
                    var val = ParseFloat(parser.Consume<Scalar>().Value);
                    switch (key)
                    {
                        case "r": r = val; break;
                        case "g": g = val; break;
                        case "b": b = val; break;
                        case "a": a = val; break;
                    }
                }
                return new Color(r, g, b, a);
            }

            public void WriteYaml(YamlEmitter emitter, object value, Type type, ObjectSerializer rootSerializer)
            {
                var c = (Color)value;
                emitter.Emit(new MappingStart(default, default, false, MappingStyle.Flow));
                EmitScalar(emitter, "r"); EmitScalar(emitter, Fmt(c.r));
                EmitScalar(emitter, "g"); EmitScalar(emitter, Fmt(c.g));
                EmitScalar(emitter, "b"); EmitScalar(emitter, Fmt(c.b));
                EmitScalar(emitter, "a"); EmitScalar(emitter, Fmt(c.a));
                emitter.Emit(new MappingEnd());
            }
        }

        // ─── SerializableCurve TypeConverter ───

        private class SerializableCurveTypeConverter : IYamlTypeConverter
        {
            public bool Accepts(Type type) => type == typeof(SerializableCurve);

            public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
            {
                var curve = new SerializableCurve();
                parser.Consume<MappingStart>();

                while (!parser.TryConsume<MappingEnd>(out _))
                {
                    var key = parser.Consume<Scalar>().Value;
                    if (key == "keyframes")
                    {
                        parser.Consume<SequenceStart>();
                        while (!parser.TryConsume<SequenceEnd>(out _))
                        {
                            parser.Consume<MappingStart>();
                            var kf = new CurveKeyframe();
                            while (!parser.TryConsume<MappingEnd>(out _))
                            {
                                var kfKey = parser.Consume<Scalar>().Value;
                                var kfVal = ParseFloat(parser.Consume<Scalar>().Value);
                                switch (kfKey)
                                {
                                    case "time": kf.Time = kfVal; break;
                                    case "value": kf.Value = kfVal; break;
                                    case "in_tangent": kf.InTangent = kfVal; break;
                                    case "out_tangent": kf.OutTangent = kfVal; break;
                                }
                            }
                            curve.Keyframes.Add(kf);
                        }
                    }
                    else
                    {
                        // Skip unknown keys
                        SkipValue(parser);
                    }
                }

                return curve;
            }

            public void WriteYaml(YamlEmitter emitter, object value, Type type, ObjectSerializer rootSerializer)
            {
                var curve = (SerializableCurve)value;
                emitter.Emit(new MappingStart());
                EmitScalar(emitter, "keyframes");
                emitter.Emit(new SequenceStart(default, default, false, SequenceStyle.Block));

                foreach (var kf in curve.Keyframes)
                {
                    emitter.Emit(new MappingStart(default, default, false, MappingStyle.Flow));
                    EmitScalar(emitter, "time"); EmitScalar(emitter, Fmt(kf.Time));
                    EmitScalar(emitter, "value"); EmitScalar(emitter, Fmt(kf.Value));
                    if (kf.InTangent != 0f)
                    {
                        EmitScalar(emitter, "in_tangent"); EmitScalar(emitter, Fmt(kf.InTangent));
                    }
                    if (kf.OutTangent != 0f)
                    {
                        EmitScalar(emitter, "out_tangent"); EmitScalar(emitter, Fmt(kf.OutTangent));
                    }
                    emitter.Emit(new MappingEnd());
                }

                emitter.Emit(new SequenceEnd());
                emitter.Emit(new MappingEnd());
            }
        }

        // ─── Stage Serialization ───

        public static string SerializeStage(Stage stage)
        {
            using var sw = new StringWriter();
            var emitter = new YamlDotNet.Core.Emitter(sw);

            emitter.Emit(new StreamStart());
            emitter.Emit(new DocumentStart());
            emitter.Emit(new MappingStart());

            EmitScalar(emitter, "id");
            EmitScalar(emitter, stage.Id);
            EmitScalar(emitter, "name");
            EmitScalar(emitter, stage.Name);
            EmitScalar(emitter, "seed");
            EmitScalar(emitter, stage.Seed.ToString());

            EmitScalar(emitter, "segments");
            emitter.Emit(new SequenceStart(default, default, false, SequenceStyle.Block));

            if (stage.Segments != null)
            {
                foreach (var seg in stage.Segments)
                {
                    emitter.Emit(new MappingStart());

                    EmitScalar(emitter, "id");
                    EmitScalar(emitter, seg.Id);
                    EmitScalar(emitter, "name");
                    EmitScalar(emitter, seg.Name);
                    EmitScalar(emitter, "type");
                    EmitScalar(emitter, PascalToSnake(seg.Type.ToString()));
                    EmitScalar(emitter, "duration");
                    EmitScalar(emitter, Fmt(seg.Duration));

                    if (seg.DesignEstimate >= 0f)
                    {
                        EmitScalar(emitter, "design_estimate");
                        EmitScalar(emitter, Fmt(seg.DesignEstimate));
                    }

                    if (seg.EntryTrigger != null)
                    {
                        EmitScalar(emitter, "entry_trigger");
                        emitter.Emit(new MappingStart());
                        EmitScalar(emitter, "type");
                        EmitScalar(emitter, PascalToSnake(seg.EntryTrigger.Type.ToString()));
                        if (seg.EntryTrigger.Params != null && seg.EntryTrigger.Params.Count > 0)
                        {
                            EmitScalar(emitter, "params");
                            emitter.Emit(new MappingStart());
                            foreach (var kvp in seg.EntryTrigger.Params)
                            {
                                EmitScalar(emitter, kvp.Key);
                                EmitScalar(emitter, FmtValue(kvp.Value));
                            }
                            emitter.Emit(new MappingEnd());
                        }
                        emitter.Emit(new MappingEnd());
                    }

                    EmitScalar(emitter, "events");
                    emitter.Emit(new SequenceStart(default, default, false, SequenceStyle.Block));

                    if (seg.Events != null)
                    {
                        foreach (var evt in seg.Events)
                        {
                            emitter.Emit(new MappingStart());

                            var tag = TypeRegistry.GetTag(evt.GetType());
                            EmitScalar(emitter, "type");
                            EmitScalar(emitter, tag);
                            EmitScalar(emitter, "id");
                            EmitScalar(emitter, evt.Id);
                            EmitScalar(emitter, "start_time");
                            EmitScalar(emitter, Fmt(evt.StartTime));
                            EmitScalar(emitter, "duration");
                            EmitScalar(emitter, Fmt(evt.Duration));

                            if (evt is SpawnPatternEvent sp)
                            {
                                EmitScalar(emitter, "pattern_id");
                                EmitScalar(emitter, sp.PatternId);
                                EmitScalar(emitter, "spawn_position");
                                emitter.Emit(new MappingStart(default, default, false, MappingStyle.Flow));
                                EmitScalar(emitter, "x"); EmitScalar(emitter, Fmt(sp.SpawnPosition.x));
                                EmitScalar(emitter, "y"); EmitScalar(emitter, Fmt(sp.SpawnPosition.y));
                                EmitScalar(emitter, "z"); EmitScalar(emitter, Fmt(sp.SpawnPosition.z));
                                emitter.Emit(new MappingEnd());
                            }
                            else if (evt is SpawnWaveEvent sw2)
                            {
                                EmitScalar(emitter, "wave_id");
                                EmitScalar(emitter, sw2.WaveId);
                                EmitScalar(emitter, "spawn_offset");
                                emitter.Emit(new MappingStart(default, default, false, MappingStyle.Flow));
                                EmitScalar(emitter, "x"); EmitScalar(emitter, Fmt(sw2.SpawnOffset.x));
                                EmitScalar(emitter, "y"); EmitScalar(emitter, Fmt(sw2.SpawnOffset.y));
                                EmitScalar(emitter, "z"); EmitScalar(emitter, Fmt(sw2.SpawnOffset.z));
                                emitter.Emit(new MappingEnd());
                            }
                            else if (evt is ActionEvent ae)
                            {
                                EmitScalar(emitter, "action_type");
                                EmitScalar(emitter, PascalToSnake(ae.ActionType.ToString()));
                                if (ae.Blocking)
                                {
                                    EmitScalar(emitter, "blocking");
                                    EmitScalar(emitter, "true");
                                    if (ae.BlockingDelay > 0f)
                                    {
                                        EmitScalar(emitter, "blocking_delay");
                                        EmitScalar(emitter, ae.BlockingDelay.ToString(System.Globalization.CultureInfo.InvariantCulture));
                                    }
                                }
                                if (ae.Params != null)
                                {
                                    EmitScalar(emitter, "params");
                                    emitter.Emit(new MappingStart());
                                    EmitObjectProperties(emitter, ae.Params, ae.Params.GetType());
                                    emitter.Emit(new MappingEnd());
                                }
                            }

                            emitter.Emit(new MappingEnd());
                        }
                    }

                    emitter.Emit(new SequenceEnd());

                    // Spell card IDs (BossFight segments)
                    if (seg.SpellCardIds != null && seg.SpellCardIds.Count > 0)
                    {
                        EmitScalar(emitter, "spell_card_ids");
                        emitter.Emit(new SequenceStart(default, default, false, SequenceStyle.Flow));
                        foreach (var scId in seg.SpellCardIds)
                            EmitScalar(emitter, scId);
                        emitter.Emit(new SequenceEnd());
                    }

                    emitter.Emit(new MappingEnd());
                }
            }

            emitter.Emit(new SequenceEnd());
            emitter.Emit(new MappingEnd());
            emitter.Emit(new DocumentEnd(true));
            emitter.Emit(new StreamEnd());

            return sw.ToString();
        }

        public static Stage DeserializeStage(string yaml)
        {
            var deserializer = new DeserializerBuilder().Build();
            var raw = deserializer.Deserialize<Dictionary<object, object>>(yaml);
            return MapStage(ToStringDict(raw));
        }

        public static void SerializeStageToFile(Stage stage, string path)
        {
            File.WriteAllText(path, SerializeStage(stage));
        }

        public static Stage DeserializeStageFromFile(string path)
        {
            return DeserializeStage(File.ReadAllText(path));
        }

        private static Dictionary<string, object> ToStringDict(object obj)
        {
            if (obj is Dictionary<object, object> raw)
            {
                var result = new Dictionary<string, object>();
                foreach (var kvp in raw)
                    result[kvp.Key.ToString()] = kvp.Value;
                return result;
            }
            if (obj is Dictionary<string, object> already)
                return already;
            return new Dictionary<string, object>();
        }

        private static Stage MapStage(Dictionary<string, object> dict)
        {
            var stage = new Stage();
            if (dict.TryGetValue("id", out var id)) stage.Id = id.ToString();
            if (dict.TryGetValue("name", out var name)) stage.Name = name.ToString();
            if (dict.TryGetValue("seed", out var seedVal))
                stage.Seed = int.TryParse(seedVal.ToString(), out var s) ? s : 0;

            if (dict.TryGetValue("segments", out var segsObj) && segsObj is List<object> segsList)
            {
                stage.Segments = new List<TimelineSegment>();
                foreach (var item in segsList)
                    stage.Segments.Add(MapSegment(ToStringDict(item)));
            }

            return stage;
        }

        private static TimelineSegment MapSegment(Dictionary<string, object> dict)
        {
            var seg = new TimelineSegment();
            if (dict.TryGetValue("id", out var id)) seg.Id = id.ToString();
            if (dict.TryGetValue("name", out var name)) seg.Name = name.ToString();
            if (dict.TryGetValue("type", out var typeVal))
                seg.Type = (SegmentType)Enum.Parse(typeof(SegmentType), SnakeToPascal(typeVal.ToString()), true);
            if (dict.TryGetValue("duration", out var dur)) seg.Duration = ParseFloat(dur);
            if (dict.TryGetValue("design_estimate", out var de)) seg.DesignEstimate = ParseFloat(de);

            if (dict.TryGetValue("entry_trigger", out var trigObj))
                seg.EntryTrigger = MapTriggerCondition(ToStringDict(trigObj));

            if (dict.TryGetValue("events", out var evtsObj) && evtsObj is List<object> evtsList)
            {
                seg.Events = new List<TimelineEvent>();
                foreach (var item in evtsList)
                    seg.Events.Add(MapTimelineEvent(ToStringDict(item)));
            }

            if (dict.TryGetValue("spell_card_ids", out var scIdsObj) && scIdsObj is List<object> scIdsList)
            {
                seg.SpellCardIds = new List<string>();
                foreach (var item in scIdsList)
                    seg.SpellCardIds.Add(item.ToString());
            }

            return seg;
        }

        private static TimelineEvent MapTimelineEvent(Dictionary<string, object> dict)
        {
            if (!dict.TryGetValue("type", out var typeVal))
                throw new YamlException("TimelineEvent missing 'type' field");

            var tag = typeVal.ToString();

            switch (tag)
            {
                case "spawn_pattern":
                    var sp = new SpawnPatternEvent();
                    if (dict.TryGetValue("id", out var id)) sp.Id = id.ToString();
                    if (dict.TryGetValue("start_time", out var st)) sp.StartTime = ParseFloat(st);
                    if (dict.TryGetValue("duration", out var dur)) sp.Duration = ParseFloat(dur);
                    if (dict.TryGetValue("pattern_id", out var pid)) sp.PatternId = pid.ToString();
                    if (dict.TryGetValue("spawn_position", out var posObj))
                    {
                        var posDict = ToStringDict(posObj);
                        sp.SpawnPosition = new Vector3(
                            posDict.TryGetValue("x", out var xv) ? ParseFloat(xv) : 0f,
                            posDict.TryGetValue("y", out var yv) ? ParseFloat(yv) : 0f,
                            posDict.TryGetValue("z", out var zv) ? ParseFloat(zv) : 0f
                        );
                    }
                    return sp;

                case "spawn_wave":
                    var sw2 = new SpawnWaveEvent();
                    if (dict.TryGetValue("id", out var wid)) sw2.Id = wid.ToString();
                    if (dict.TryGetValue("start_time", out var wst)) sw2.StartTime = ParseFloat(wst);
                    if (dict.TryGetValue("duration", out var wdur)) sw2.Duration = ParseFloat(wdur);
                    if (dict.TryGetValue("wave_id", out var waveid)) sw2.WaveId = waveid.ToString();
                    if (dict.TryGetValue("spawn_offset", out var offObj))
                    {
                        var offDict = ToStringDict(offObj);
                        sw2.SpawnOffset = new Vector3(
                            offDict.TryGetValue("x", out var ox) ? ParseFloat(ox) : 0f,
                            offDict.TryGetValue("y", out var oy) ? ParseFloat(oy) : 0f,
                            offDict.TryGetValue("z", out var oz) ? ParseFloat(oz) : 0f
                        );
                    }
                    return sw2;

                case "action":
                    var ae = new ActionEvent();
                    if (dict.TryGetValue("id", out var aid)) ae.Id = aid.ToString();
                    if (dict.TryGetValue("start_time", out var ast)) ae.StartTime = ParseFloat(ast);
                    if (dict.TryGetValue("duration", out var adur)) ae.Duration = ParseFloat(adur);
                    if (dict.TryGetValue("action_type", out var atVal))
                        ae.ActionType = (ActionType)System.Enum.Parse(typeof(ActionType), SnakeToPascal(atVal.ToString()), true);
                    if (dict.TryGetValue("blocking", out var blk))
                        ae.Blocking = blk.ToString().Equals("true", System.StringComparison.OrdinalIgnoreCase);
                    if (dict.TryGetValue("blocking_delay", out var bdelay))
                        ae.BlockingDelay = ParseFloat(bdelay);
                    if (dict.TryGetValue("params", out var paramsObj2))
                    {
                        var paramsType = ActionParamsRegistry.Resolve(ae.ActionType);
                        if (paramsType != null)
                        {
                            var paramsInstance = (IActionParams)System.Activator.CreateInstance(paramsType);
                            var paramsDict = ToStringDict(paramsObj2);
                            ApplyProperties(paramsInstance, paramsType, paramsDict);
                            ae.Params = paramsInstance;
                        }
                    }
                    return ae;

                default:
                    throw new YamlException($"Unknown TimelineEvent type: '{tag}'");
            }
        }

        private static TriggerCondition MapTriggerCondition(Dictionary<string, object> dict)
        {
            var trigger = new TriggerCondition();
            if (dict.TryGetValue("type", out var typeVal))
                trigger.Type = (TriggerType)Enum.Parse(typeof(TriggerType), SnakeToPascal(typeVal.ToString()), true);

            if (dict.TryGetValue("params", out var paramsObj))
            {
                trigger.Params = new Dictionary<string, object>();
                var paramsDict = ToStringDict(paramsObj);
                foreach (var kvp in paramsDict)
                    trigger.Params[kvp.Key] = kvp.Value;
            }

            return trigger;
        }

        // ─── Shared Helpers ───

        /// <summary>
        /// Read a YAML mapping into a Dictionary. Values can be:
        /// string (scalar), Vector3 (mapping with x/y/z), SerializableCurve (mapping with keyframes),
        /// or List of IModifier (sequence).
        /// Handles nested structures by detecting the next parser event type.
        /// </summary>
        private static Dictionary<string, object> ReadMapping(IParser parser)
        {
            parser.Consume<MappingStart>();
            var result = new Dictionary<string, object>();

            while (!parser.TryConsume<MappingEnd>(out _))
            {
                var key = parser.Consume<Scalar>().Value;
                result[key] = ReadValue(parser);
            }

            return result;
        }

        /// <summary>
        /// Read a single YAML value, dispatching by event type.
        /// </summary>
        private static object ReadValue(IParser parser)
        {
            if (parser.Accept<Scalar>(out _))
            {
                return parser.Consume<Scalar>().Value;
            }

            if (parser.Accept<MappingStart>(out _))
            {
                // Could be Vector3, SerializableCurve, or generic mapping
                return ReadMapping(parser);
            }

            if (parser.Accept<SequenceStart>(out _))
            {
                return ReadSequence(parser);
            }

            // Skip anything else
            SkipValue(parser);
            return null;
        }

        private static List<object> ReadSequence(IParser parser)
        {
            parser.Consume<SequenceStart>();
            var list = new List<object>();
            while (!parser.TryConsume<SequenceEnd>(out _))
            {
                list.Add(ReadValue(parser));
            }
            return list;
        }

        /// <summary>
        /// Apply parsed properties to an object instance via reflection.
        /// Handles scalar types, Vector3 (from nested dict), SerializableCurve (from nested dict).
        /// </summary>
        private static void ApplyProperties(object instance, Type type,
            Dictionary<string, object> properties)
        {
            foreach (var kvp in properties)
            {
                if (kvp.Key == "type") continue; // Already consumed

                var propName = SnakeToPascal(kvp.Key);
                var prop = type.GetProperty(propName);
                if (prop == null) continue;

                prop.SetValue(instance, ConvertToType(kvp.Value, prop.PropertyType));
            }
        }

        /// <summary>
        /// Convert a parsed YAML value to the target C# type.
        /// </summary>
        private static object ConvertToType(object value, Type targetType)
        {
            if (value == null) return null;

            // String scalar → primitive conversion
            if (value is string s)
            {
                if (targetType == typeof(int)) return int.Parse(s, Inv);
                if (targetType == typeof(float)) return float.Parse(s, Inv);
                if (targetType == typeof(string)) return s;
                if (targetType == typeof(bool)) return bool.Parse(s);
                if (targetType.IsEnum)
                {
                    // YAML uses snake_case, enums use PascalCase
                    var pascalVal = SnakeToPascal(s);
                    return Enum.Parse(targetType, pascalVal, true);
                }
                return s;
            }

            // Nested mapping → Vector2, Vector3, Color, SerializableCurve, or custom object
            if (value is Dictionary<string, object> dict || value is Dictionary<object, object>)
            {
                var d = value is Dictionary<string, object> sd ? sd : ToStringDict(value);
                if (targetType == typeof(Vector3))
                {
                    return new Vector3(
                        d.TryGetValue("x", out var xv) ? ParseFloat(xv) : 0f,
                        d.TryGetValue("y", out var yv) ? ParseFloat(yv) : 0f,
                        d.TryGetValue("z", out var zv) ? ParseFloat(zv) : 0f
                    );
                }

                if (targetType == typeof(Vector2))
                {
                    return new Vector2(
                        d.TryGetValue("x", out var x2v) ? ParseFloat(x2v) : 0f,
                        d.TryGetValue("y", out var y2v) ? ParseFloat(y2v) : 0f
                    );
                }

                if (targetType == typeof(Color))
                {
                    return new Color(
                        d.TryGetValue("r", out var rv) ? ParseFloat(rv) : 1f,
                        d.TryGetValue("g", out var gv) ? ParseFloat(gv) : 1f,
                        d.TryGetValue("b", out var bv) ? ParseFloat(bv) : 1f,
                        d.TryGetValue("a", out var av) ? ParseFloat(av) : 1f
                    );
                }

                if (targetType == typeof(SerializableCurve))
                {
                    return ConvertToCurve(d);
                }

                // Generic nested object: instantiate and apply properties
                if (targetType.IsClass && targetType != typeof(string))
                {
                    var nested = Activator.CreateInstance(targetType);
                    ApplyProperties(nested, targetType, d);
                    return nested;
                }
            }

            // List<T> from YAML sequence
            if (value is List<object> list && targetType.IsGenericType
                && targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elemType = targetType.GetGenericArguments()[0];
                var typedList = (System.Collections.IList)Activator.CreateInstance(targetType);
                foreach (var item in list)
                {
                    typedList.Add(ConvertToType(item, elemType));
                }
                return typedList;
            }

            return value;
        }

        private static SerializableCurve ConvertToCurve(Dictionary<string, object> dict)
        {
            var curve = new SerializableCurve();
            if (dict.TryGetValue("keyframes", out var kfObj) && kfObj is List<object> kfList)
            {
                foreach (var item in kfList)
                {
                    if (item is Dictionary<string, object> kfDict)
                    {
                        var kf = new CurveKeyframe();
                        if (kfDict.TryGetValue("time", out var t)) kf.Time = ParseFloat(t);
                        if (kfDict.TryGetValue("value", out var v)) kf.Value = ParseFloat(v);
                        if (kfDict.TryGetValue("in_tangent", out var it)) kf.InTangent = ParseFloat(it);
                        if (kfDict.TryGetValue("out_tangent", out var ot)) kf.OutTangent = ParseFloat(ot);
                        curve.Keyframes.Add(kf);
                    }
                }
            }
            return curve;
        }

        /// <summary>Emit all public properties of an object, skipping non-serializable ones.</summary>
        private static void EmitObjectProperties(YamlEmitter emitter, object value, Type type)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.Name == "TypeName" || prop.Name == "RequiresSimulation") continue;
                if (prop.Name == "Rng") continue; // Runtime-only, not serialized
                if (!prop.CanRead) continue;

                var val = prop.GetValue(value);
                if (val == null) continue;

                EmitScalar(emitter, PascalToSnake(prop.Name));
                EmitValue(emitter, val);
            }
        }

        /// <summary>Emit a value, dispatching by runtime type.</summary>
        private static void EmitValue(YamlEmitter emitter, object val)
        {
            switch (val)
            {
                case Vector3 v3:
                    emitter.Emit(new MappingStart(default, default, false, MappingStyle.Flow));
                    EmitScalar(emitter, "x"); EmitScalar(emitter, Fmt(v3.x));
                    EmitScalar(emitter, "y"); EmitScalar(emitter, Fmt(v3.y));
                    EmitScalar(emitter, "z"); EmitScalar(emitter, Fmt(v3.z));
                    emitter.Emit(new MappingEnd());
                    break;

                case Vector2 v2:
                    emitter.Emit(new MappingStart(default, default, false, MappingStyle.Flow));
                    EmitScalar(emitter, "x"); EmitScalar(emitter, Fmt(v2.x));
                    EmitScalar(emitter, "y"); EmitScalar(emitter, Fmt(v2.y));
                    emitter.Emit(new MappingEnd());
                    break;

                case Color c:
                    emitter.Emit(new MappingStart(default, default, false, MappingStyle.Flow));
                    EmitScalar(emitter, "r"); EmitScalar(emitter, Fmt(c.r));
                    EmitScalar(emitter, "g"); EmitScalar(emitter, Fmt(c.g));
                    EmitScalar(emitter, "b"); EmitScalar(emitter, Fmt(c.b));
                    EmitScalar(emitter, "a"); EmitScalar(emitter, Fmt(c.a));
                    emitter.Emit(new MappingEnd());
                    break;

                case SerializableCurve curve:
                    emitter.Emit(new MappingStart());
                    EmitScalar(emitter, "keyframes");
                    emitter.Emit(new SequenceStart(default, default, false, SequenceStyle.Block));
                    foreach (var kf in curve.Keyframes)
                    {
                        emitter.Emit(new MappingStart(default, default, false, MappingStyle.Flow));
                        EmitScalar(emitter, "time"); EmitScalar(emitter, Fmt(kf.Time));
                        EmitScalar(emitter, "value"); EmitScalar(emitter, Fmt(kf.Value));
                        if (kf.InTangent != 0f)
                        {
                            EmitScalar(emitter, "in_tangent"); EmitScalar(emitter, Fmt(kf.InTangent));
                        }
                        if (kf.OutTangent != 0f)
                        {
                            EmitScalar(emitter, "out_tangent"); EmitScalar(emitter, Fmt(kf.OutTangent));
                        }
                        emitter.Emit(new MappingEnd());
                    }
                    emitter.Emit(new SequenceEnd());
                    emitter.Emit(new MappingEnd());
                    break;

                default:
                    // List<T> → YAML sequence
                    if (val is System.Collections.IList valList && val.GetType().IsGenericType)
                    {
                        emitter.Emit(new SequenceStart(default, default, false, SequenceStyle.Block));
                        foreach (var item in valList)
                        {
                            if (item == null) continue;
                            var itemType = item.GetType();
                            if (itemType.IsPrimitive || itemType == typeof(string) || itemType.IsEnum)
                            {
                                EmitScalar(emitter, FmtValue(item));
                            }
                            else
                            {
                                emitter.Emit(new MappingStart(default, default, false, MappingStyle.Block));
                                EmitObjectProperties(emitter, item, itemType);
                                emitter.Emit(new MappingEnd());
                            }
                        }
                        emitter.Emit(new SequenceEnd());
                        break;
                    }
                    // Nested custom object → YAML mapping
                    if (val.GetType().IsClass && val.GetType() != typeof(string)
                        && !val.GetType().IsPrimitive)
                    {
                        emitter.Emit(new MappingStart(default, default, false, MappingStyle.Block));
                        EmitObjectProperties(emitter, val, val.GetType());
                        emitter.Emit(new MappingEnd());
                        break;
                    }
                    EmitScalar(emitter, FmtValue(val));
                    break;
            }
        }

        /// <summary>Skip a YAML value (scalar, mapping, or sequence) without consuming it into data.</summary>
        private static void SkipValue(IParser parser)
        {
            parser.SkipThisAndNestedEvents();
        }

        private static void EmitScalar(YamlEmitter emitter, string value)
        {
            emitter.Emit(new Scalar(value));
        }

        private static readonly System.Globalization.CultureInfo Inv =
            System.Globalization.CultureInfo.InvariantCulture;

        private static string Fmt(float v) => v.ToString("G", Inv);

        private static float ParseFloat(string s) => float.Parse(s, Inv);

        private static float ParseFloat(object o)
        {
            if (o is string s) return float.Parse(s, Inv);
            if (o is float f) return f;
            if (o is double d) return (float)d;
            if (o is int i) return i;
            return 0f;
        }

        private static string FmtValue(object val)
        {
            if (val is float f) return Fmt(f);
            if (val is int i) return i.ToString();
            if (val is bool b) return b ? "true" : "false";
            if (val is Enum) return PascalToSnake(val.ToString());
            return val.ToString();
        }

        /// <summary>Convert snake_case to PascalCase.</summary>
        private static string SnakeToPascal(string snake)
        {
            var parts = snake.Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                    parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
            }
            return string.Join("", parts);
        }

        /// <summary>Convert PascalCase to snake_case.</summary>
        private static string PascalToSnake(string pascal)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < pascal.Length; i++)
            {
                if (char.IsUpper(pascal[i]))
                {
                    if (i > 0) sb.Append('_');
                    sb.Append(char.ToLower(pascal[i]));
                }
                else
                {
                    sb.Append(pascal[i]);
                }
            }
            return sb.ToString();
        }
    }
}
