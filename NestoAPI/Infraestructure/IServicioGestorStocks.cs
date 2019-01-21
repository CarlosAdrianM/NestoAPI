using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public interface IServicioGestorStocks
    {
        int Stock(string producto, string almacen);

        int UnidadesPendientesEntregarAlmacen(string producto, string almacen);

        int UnidadesDisponiblesTodosLosAlmacenes(string producto);
    }
}
