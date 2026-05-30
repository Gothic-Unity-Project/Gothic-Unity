using System;
using System.Collections.Generic;
using System.Numerics;
using ZenKit;
using ZenKit.Util;
using ZenKit.Vobs;

namespace GUZ.Core.Models.Proxy
{
    public class VirtualObjectProxy : IVirtualObject
    {
        protected IVirtualObject Vob;

        public VirtualObjectProxy(IVirtualObject vob)
        {
            Vob = vob;
        }

        public IVirtualObject GetVob()
        {
            return Vob;
        }
        
        public IVirtualObject Cache()
        {
            throw new NotImplementedException("We would also need to wrap the object again. Will be implemented when really needed.");
        }

        public bool IsCached()
        {
            return Vob.IsCached();
        }

        public IVirtualObject GetChild(int i)
        {
            return Vob.GetChild(i);
        }

        public void AddChild(IVirtualObject obj)
        {
            Vob.AddChild(obj);
        }

        public void RemoveChild(int i)
        {
            Vob.RemoveChild(i);
        }

        public void RemoveChildren(Predicate<IVirtualObject> pred)
        {
            Vob.RemoveChildren(pred);
        }
        
        public VirtualObjectType Type => Vob.Type;
        public int Id => Vob.Id;
        public AxisAlignedBoundingBox BoundingBox { get => Vob.BoundingBox; set => Vob.BoundingBox = value; }
        public Vector3 Position { get => Vob.Position; set => Vob.Position = value; }
        public Matrix3x3 Rotation { get => Vob.Rotation; set => Vob.Rotation = value; }
        public bool ShowVisual { get => Vob.ShowVisual; set => Vob.ShowVisual = value; }
        public SpriteAlignment SpriteCameraFacingMode { get => Vob.SpriteCameraFacingMode; set => Vob.SpriteCameraFacingMode = value; }
        public bool CdStatic { get => Vob.CdStatic; set => Vob.CdStatic = value; }
        public bool CdDynamic { get => Vob.CdDynamic; set => Vob.CdDynamic = value; }
        public bool Static { get => Vob.Static; set => Vob.Static = value; }
        public ShadowType DynamicShadows { get => Vob.DynamicShadows; set => Vob.DynamicShadows = value; }
        public bool PhysicsEnabled { get => Vob.PhysicsEnabled; set => Vob.PhysicsEnabled = value; }
        public AnimationType AnimationType { get => Vob.AnimationType; set => Vob.AnimationType = value; }
        public int Bias { get => Vob.Bias; set => Vob.Bias = value; }
        public bool Ambient { get => Vob.Ambient; set => Vob.Ambient = value; }
        public float AnimationStrength { get => Vob.AnimationStrength; set => Vob.AnimationStrength = value; }
        public float FarClipScale { get => Vob.FarClipScale; set => Vob.FarClipScale = value; }
        public string PresetName { get => Vob.PresetName; set => Vob.PresetName = value; }
        public string Name { get => Vob.Name; set => Vob.Name = value; }
        public IVisual Visual { get => Vob.Visual; set => Vob.Visual = value; }
        public byte SleepMode { get => Vob.SleepMode; set => Vob.SleepMode = value; }
        public float NextOnTimer { get => Vob.NextOnTimer; set => Vob.NextOnTimer = value; }
        public IAi Ai { get => Vob.Ai; set => Vob.Ai = value; }
        public IEventManager EventManager { get => Vob.EventManager; set => Vob.EventManager = value; }
        public int ChildCount => Vob.ChildCount;
        public List<IVirtualObject> Children => Vob.Children;
    }
}
