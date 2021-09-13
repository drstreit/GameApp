using System;

namespace ALogger
{
    public class LogMessage
    {
        public Levels Level;
        public object Message;

        public LogMessage(Levels level, object message)
        {
            Level = level;
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }
    }
}
