using System;

namespace SmartFillMonitorPractice.Services
{
    public class BusinessException : Exception
    {
        public BusinessException(string message) : base(message)
        {
        }
    }

    public class AuthorizationException : BusinessException
    {
        public AuthorizationException(string message) : base(message)
        {
        }
    }

    public class InfrastructureException : Exception
    {
        public InfrastructureException(string message, Exception? innerException = null) : base(message, innerException)
        {
        }
    }
}
