using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Models.Picking
{
    public interface IRellenadorPickingService
    {
        List<PedidoPicking> Rellenar();
        List<PedidoPicking> Rellenar(List<Ruta> rutas);
        List<PedidoPicking> Rellenar(string cliente);
        List<PedidoPicking> Rellenar(string empresa, int numeroPedido);
        List<LineaPedidoPicking> RellenarTodasLasLineas(List<PedidoPicking> candidatos);
    }
}
