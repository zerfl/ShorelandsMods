namespace ShorelandsMods.Core.Areas
{
    internal sealed class ColorArea : Area
    {
        public override string Name => "Colours";

        protected internal override void Register(AreaSetup s)
        {
            s.Function(
                "ColorFromHex( hex:string ) -> color:table|nil",
                "\"#rrggbb\" or \"#rrggbbaa\" as { r, g, b, a } from 0 to 1; nil for a text that is not such a colour. "
                    + "Colour arguments of other functions take the hex text too",
                c =>
                {
                    if (!(c.Color(1, "hex") is Rgba color))
                        return c.Return(Lua.LuaValue.Nil);
                    var t = new Lua.LuaTable(0, 4);
                    t["r"] = color.R;
                    t["g"] = color.G;
                    t["b"] = color.B;
                    t["a"] = color.A;
                    return c.Return(t);
                }
            );
        }
    }
}
