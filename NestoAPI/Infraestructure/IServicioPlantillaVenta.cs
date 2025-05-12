using NestoAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public interface IServicioPlantillaVenta
    {
        Task<List<LineaPlantillaVenta>> BusquedaContextual(string filtroProducto);
        HashSet<string> CargarProductosBonificables();
        HashSet<string> CargarProductosYaComprados(string cliente);
    }
}