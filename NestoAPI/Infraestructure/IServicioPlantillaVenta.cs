using NestoAPI.Models;
using System.Collections.Generic;

namespace NestoAPI.Infraestructure
{
    public interface IServicioPlantillaVenta
    {
        HashSet<string> CargarProductosBonificables();
        HashSet<string> CargarProductosYaComprados(string cliente);
    }
}