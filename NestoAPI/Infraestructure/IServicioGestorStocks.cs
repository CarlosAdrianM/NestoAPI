using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public interface IServicioGestorStocks
    {
        int Stock(string producto);
        int Stock(string producto, string almacen);
        int UnidadesPendientesEntregar(string producto);
        int UnidadesPendientesEntregarAlmacen(string producto, string almacen);
        int UnidadesDisponiblesTodosLosAlmacenes(string producto);
        /// <summary>
        /// NestoAPI#517: todo lo que necesita ColorStock para un conjunto de productos, en unas pocas
        /// consultas agrupadas (en vez de 2-5 consultas por producto).
        /// </summary>
        ResumenStocksProductos LeerResumenStocks(IEnumerable<string> productos);
    }
}
