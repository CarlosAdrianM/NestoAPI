using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Models.Picking
{
    public interface IRellenadorUbicacionesService
    {
        List<UbicacionPicking> Rellenar(List<PedidoPicking> pedidos);
    }
}
