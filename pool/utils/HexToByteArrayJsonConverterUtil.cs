﻿using System;
using System.Globalization;
using XPool.extensions;
using Newtonsoft.Json;

namespace XPool.utils
{
    public class HexToByteArrayJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(byte[]) == objectType;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue($"0x{value:x}");
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var str = (string) reader.Value;
            if (str.StartsWith("0x"))
                str = str.Substring(2);

            if (string.IsNullOrEmpty(str))
                return null;

            return str.HexToByteArray();
        }
    }
}
