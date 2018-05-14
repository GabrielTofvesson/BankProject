using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Client
{
    public class ParseException : SystemException
    {
        public ParseException()
        {
        }

        public ParseException(string message) : base(message)
        {
        }

        public ParseException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected ParseException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
