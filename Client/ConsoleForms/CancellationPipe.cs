using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.ConsoleForms
{
    public sealed class CancellationPipe
    {
        private bool cancel = false;
        public bool Cancelled
        {
            get => cancel;
            set => cancel |= value;
        }

        // Redundant
        public void Cancel() => Cancelled = true;
    }
}
