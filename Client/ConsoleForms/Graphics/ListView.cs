using Client.ConsoleForms.Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Client.ConsoleForms.Graphics
{
    public class ListView : View
    {


        public ListView(ViewData parameters) : base(parameters)
        {

        }

        public override Region Occlusion => throw new NotImplementedException();

        protected override void _Draw(int left, int top)
        {
            throw new NotImplementedException();
        }
    }
}
