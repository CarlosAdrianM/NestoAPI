using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Models.Picking
{
    public interface IFinalizadorPicking
    {
        void Ejecutar(NVEntities db);
    }
}
