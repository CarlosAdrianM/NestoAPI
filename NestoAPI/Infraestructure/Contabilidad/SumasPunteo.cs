using NestoAPI.Models;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Contabilidad
{
    /// <summary>
    /// Importe ya punteado de cada apunte (banco o contabilidad) en la conciliación bancaria.
    /// 23/09/26: antes se preguntaba apunte a apunte (dos consultas por apunte de banco, una por
    /// apunte de contabilidad): la ventana Bancos tardaba ~10 s en abrir La Caixa (555 apuntes en
    /// 45 días = 1.110 consultas). Ahora es una consulta agrupada por cada lote de ids.
    /// </summary>
    internal static class SumasPunteo
    {
        // Contains() de EF6 genera un IN con los valores: se acota para no mandar miles de golpe.
        private const int TAMANO_LOTE = 500;

        internal static async Task<Dictionary<int, decimal>> PorApunteBancoAsync(IQueryable<ConciliacionBancariaPunteo> punteos, IEnumerable<int> ids)
        {
            var sumas = new Dictionary<int, decimal>();
            foreach (List<int> lote in Lotes(ids))
            {
                var parciales = await punteos
                    .Where(p => p.ApunteBancoId != null && lote.Contains(p.ApunteBancoId.Value))
                    .GroupBy(p => p.ApunteBancoId.Value)
                    .Select(g => new { Id = g.Key, Suma = g.Sum(p => p.ImportePunteado) })
                    .ToListAsync()
                    .ConfigureAwait(false);
                foreach (var parcial in parciales)
                {
                    sumas[parcial.Id] = parcial.Suma;
                }
            }
            return sumas;
        }

        internal static async Task<Dictionary<int, decimal>> PorApunteContabilidadAsync(IQueryable<ConciliacionBancariaPunteo> punteos, IEnumerable<int> ids)
        {
            var sumas = new Dictionary<int, decimal>();
            foreach (List<int> lote in Lotes(ids))
            {
                var parciales = await punteos
                    .Where(p => p.ApunteContabilidadId != null && lote.Contains(p.ApunteContabilidadId.Value))
                    .GroupBy(p => p.ApunteContabilidadId.Value)
                    .Select(g => new { Id = g.Key, Suma = g.Sum(p => p.ImportePunteado) })
                    .ToListAsync()
                    .ConfigureAwait(false);
                foreach (var parcial in parciales)
                {
                    sumas[parcial.Id] = parcial.Suma;
                }
            }
            return sumas;
        }

        internal static decimal SumaDe(Dictionary<int, decimal> sumas, int id)
            => sumas.TryGetValue(id, out decimal suma) ? suma : 0;

        private static IEnumerable<List<int>> Lotes(IEnumerable<int> ids)
        {
            List<int> distintos = (ids ?? Enumerable.Empty<int>()).Distinct().ToList();
            for (int inicio = 0; inicio < distintos.Count; inicio += TAMANO_LOTE)
            {
                yield return distintos.Skip(inicio).Take(TAMANO_LOTE).ToList();
            }
        }
    }
}
