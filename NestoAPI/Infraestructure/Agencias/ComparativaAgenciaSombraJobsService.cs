using System;
using System.Collections.Generic;
using System.Linq;
using Elmah;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// Rellena la tabla ComparativaAgenciaSombra: por cada envío real reciente, calcula con el
    /// comparador qué agencia SOMBRA (p.ej. CTT) lo habría ganado y a qué coste, frente a la agencia
    /// que se usó de verdad. Así medimos —con datos reales— cuántos envíos e importe captaría una
    /// agencia antes de negociar con ella, sin que llegue a seleccionarse nunca.
    ///
    /// Idempotente: cada envío se registra una sola vez (UNIQUE en NumeroEnvio); reejecutar solo
    /// añade los envíos nuevos. Se ejecuta como job nocturno de Hangfire y es backfilleable bajo
    /// demanda desde un endpoint.
    /// </summary>
    public class ComparativaAgenciaSombraJobsService
    {
        private readonly NVEntities _db;

        public ComparativaAgenciaSombraJobsService(NVEntities db)
        {
            _db = db;
        }

        /// <summary>Punto de entrada para Hangfire (job recurrente). Procesa los últimos días.</summary>
        public static void ProcesarComparativaDiaria()
        {
            try
            {
                using (var db = new NVEntities())
                {
                    // 3 días: cubre envíos que se crean pendientes (sin peso) y se finalizan un día
                    // o dos después. La idempotencia evita duplicados al solaparse las ventanas.
                    new ComparativaAgenciaSombraJobsService(db).RegistrarComparativas(3);
                }
            }
            catch (Exception)
            {
                throw; // Re-lanzar para que Hangfire lo registre y reintente.
            }
        }

        /// <summary>
        /// Registra la comparativa de los envíos reales (con peso y ya en curso) de los últimos
        /// <paramref name="dias"/> días que aún no estén registrados. Devuelve cuántas filas insertó.
        /// </summary>
        public int RegistrarComparativas(int dias)
        {
            var idsSombra = _db.AgenciasTransportes.Where(a => a.EsSombra).Select(a => a.Numero).ToList();
            if (!idsSombra.Any())
            {
                return 0; // No hay agencias sombra: nada que medir.
            }

            var numerosExistentes = _db.AgenciasTransportes.Select(a => a.Numero).Distinct().ToList();
            var registro = new RegistroTarifasExistentes(new RegistroTarifas(), numerosExistentes);
            var comparador = new ComparadorAgencias(registro, new ProveedorRecargoCombustibleEF(_db), idsSombra);

            var nombresAgencia = _db.AgenciasTransportes
                .Select(a => new { a.Numero, a.Nombre })
                .ToList()
                .GroupBy(a => a.Numero)
                .ToDictionary(g => g.Key, g => g.First().Nombre);

            DateTime fechaDesde = DateTime.Today.AddDays(-dias);
            var yaRegistrados = new HashSet<int>(_db.ComparativaAgenciaSombras.Select(c => c.NumeroEnvio));

            // Solo envíos reales (en curso o tramitados) y con peso conocido: los pendientes sin peso
            // aún no representan un envío facturable y se registrarán cuando se finalicen.
            var envios = _db.EnviosAgencias
                .Where(e => e.Fecha >= fechaDesde
                            && e.Estado >= Constantes.Agencias.ESTADO_EN_CURSO
                            && e.Peso > 0)
                .ToList();

            int insertados = 0;
            foreach (var envio in envios)
            {
                if (yaRegistrados.Contains(envio.Numero))
                {
                    continue;
                }

                try
                {
                    _db.ComparativaAgenciaSombras.Add(Construir(envio, comparador, idsSombra, nombresAgencia));
                    _db.SaveChanges();
                    insertados++;
                }
                catch (Exception ex)
                {
                    // Best-effort por fila: un envío problemático no debe abortar el lote.
                    ErrorLog.GetDefault(null)?.Log(new Error(new Exception(
                        $"[ComparativaSombra] Error registrando el envío {envio.Numero}: {ex.Message}", ex)));
                }
            }

            return insertados;
        }

        private ComparativaAgenciaSombra Construir(EnviosAgencia envio, ComparadorAgencias comparador,
            ICollection<int> idsSombra, IReadOnlyDictionary<int, string> nombresAgencia)
        {
            string codigoPostal = envio.CodPostal?.Trim() ?? string.Empty;
            // Reembolso centinela (<0 = no cobrar) no cuenta como contra reembolso para el cálculo.
            decimal reembolso = envio.Reembolso > 0 ? envio.Reembolso : 0m;

            var ranking = comparador.Ranking(envio.Empresa, codigoPostal, envio.Peso, reembolso);

            OpcionEnvioAgencia real = ranking.FirstOrDefault(o => o.AgenciaId == envio.Agencia);
            OpcionEnvioAgencia sombra = ranking.FirstOrDefault(o => idsSombra.Contains(o.AgenciaId));

            decimal? costeReal = real?.Coste;
            decimal? costeSombra = sombra?.Coste;
            bool sombraGana = costeSombra.HasValue && (!costeReal.HasValue || costeSombra.Value < costeReal.Value);
            decimal? ahorro = (costeReal.HasValue && costeSombra.HasValue)
                ? Math.Round(costeReal.Value - costeSombra.Value, 2)
                : (decimal?)null;

            return new ComparativaAgenciaSombra
            {
                NumeroEnvio = envio.Numero,
                Empresa = envio.Empresa,
                Pedido = envio.Pedido,
                FechaEnvio = envio.Fecha,
                CodigoPostal = codigoPostal,
                Zona = CalculadoraZonaEnvio.CalcularZona(codigoPostal).ToString(),
                Peso = envio.Peso,
                Reembolso = reembolso,
                AgenciaRealId = envio.Agencia,
                AgenciaRealNombre = NombreDe(nombresAgencia, envio.Agencia),
                CosteReal = costeReal.HasValue ? Math.Round(costeReal.Value, 2) : (decimal?)null,
                AgenciaSombraId = sombra?.AgenciaId,
                AgenciaSombraNombre = sombra != null ? NombreDe(nombresAgencia, sombra.AgenciaId) : null,
                CosteSombra = costeSombra.HasValue ? Math.Round(costeSombra.Value, 2) : (decimal?)null,
                SombraGana = sombraGana,
                Ahorro = ahorro,
                FechaCalculo = DateTime.Now
            };
        }

        private static string NombreDe(IReadOnlyDictionary<int, string> nombres, int agenciaId)
            => nombres.TryGetValue(agenciaId, out string nombre) ? nombre?.Trim() : null;
    }
}
