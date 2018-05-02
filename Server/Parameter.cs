namespace Server
{
    public struct Parameter
    {
        public enum ParamType { STRING, NUMBER, BOOLEAN, NONE }
        public readonly ParamType type;
        public readonly string name;
        public readonly char flag;
        public readonly bool optional;

        public Parameter(string name, char flag, ParamType type, bool optional = false)
        {
            this.name = name;
            this.flag = flag;
            this.type = type;
            this.optional = optional;
        }

        // Easy shortcut to create parameterless flags
        public static Parameter Flag(char flagChar, bool optional = true) => new Parameter("", flagChar, ParamType.NONE, optional);
    }
}
