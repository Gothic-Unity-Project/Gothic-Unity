using System;

namespace GUZ.Core.Models.Marvin
{
    public class MarvinProperty<T>
    {
        public string Name { get; }
        public Func<T> Getter { get; }
        public Action<T> Setter { get; }
        public T MinValue { get; }
        public T MaxValue { get; }

        public MarvinProperty(string name, Func<T> getter, Action<T> setter, T minValue = default, T maxValue = default)
        {
            Name = name;
            Getter = getter;
            Setter = setter;
            MinValue = minValue;
            MaxValue = maxValue;
        }

        public T GetValue() => Getter();
        public void SetValue(T value) => Setter(value);
    }
}
