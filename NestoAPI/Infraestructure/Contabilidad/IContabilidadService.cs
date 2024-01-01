using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Contabilidad
{
    public interface IContabilidadService
    {       
        Task<int> ContabilizarDiario(string empresa, string diario, string usuario);
        Task<int> ContabilizarDiario(NVEntities db, string empresa, string diario, string usuario);
        Task<int> CrearLineas(List<PreContabilidad> lineas);
        Task<int> CrearLineas(NVEntities db, List<PreContabilidad> lineas);
        Task<int> CrearLineasYContabilizarDiario(List<PreContabilidad> lineas);
    }
}
