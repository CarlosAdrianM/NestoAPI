using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NestoAPI.Models.Informes.SaldoCuenta555;

namespace NestoAPI.Infraestructure.Informes
{
    /// <summary>
    /// Motor puro para NestoAPI#164 (Nesto#349 Fase 4): calcula el saldo acumulado de una
    /// cuenta 555 a una fecha de corte y devuelve los grupos de apuntes que quedan abiertos
    /// (no se han compensado). Los grupos que saldan a 0 no aparecen.
    ///
    /// Algoritmo en 3 pasadas secuenciales sobre apuntes cargados del año de corte:
    ///   1. AmazonOrderId (regex \d{3}-\d{7}-\d{7} sobre Concepto). Solo cierra si el grupo
    ///      tiene al menos un DEBE y un HABER. Si es solo-DEBE o solo-HABER, libera a
    ///      pasadas siguientes.
    ///   2. NumeroDocumento común. Mismo criterio: cierra solo grupos con DEBE y HABER.
    ///   3. FIFO por fecha sobre el residuo. Importes se normalizan a ImporteNeto = Debe - Haber
    ///      (signed) para tratar uniformemente apuntes con Debe negativo que funcionalmente
    ///      son HABER.
    /// </summary>
    public static class MotorSaldoCuenta
    {
        private static readonly Regex RegexAmazonOrderId = new Regex(
            @"\d{3}-\d{7}-\d{7}", RegexOptions.Compiled);

        public static SaldoCuenta555ResultadoDto Calcular(
            IEnumerable<ApunteCuentaDto> apuntes,
            string empresa,
            string cuenta,
            DateTime fechaCorte)
        {
            var listaOrdenada = (apuntes ?? Enumerable.Empty<ApunteCuentaDto>())
                .OrderBy(a => a.Fecha).ThenBy(a => a.NumeroOrden)
                .ToList();

            decimal saldoTotal = listaOrdenada.Sum(a => a.ImporteNeto);
            var grupos = new List<GrupoAbiertoDto>();

            // Pasada 1: AmazonOrderId
            var residual = ProcesarPasadaPorClave(
                listaOrdenada,
                extraerClave: a =>
                {
                    var m = RegexAmazonOrderId.Match(a.Concepto ?? string.Empty);
                    return m.Success ? m.Value : null;
                },
                tipoClave: TipoClaveGrupo.AmazonOrderId,
                fechaCorte: fechaCorte,
                gruposDestino: grupos);

            // Pasada 2: NumeroDocumento
            residual = ProcesarPasadaPorClave(
                residual,
                extraerClave: a => string.IsNullOrWhiteSpace(a.NumeroDocumento) ? null : a.NumeroDocumento,
                tipoClave: TipoClaveGrupo.NumeroDocumento,
                fechaCorte: fechaCorte,
                gruposDestino: grupos);

            // Pasada 3: FIFO sobre residuo
            grupos.AddRange(FifoEmparejar(residual, fechaCorte));

            return new SaldoCuenta555ResultadoDto
            {
                Empresa = empresa,
                Cuenta = cuenta,
                FechaCorte = fechaCorte,
                SaldoTotal = saldoTotal,
                GruposAbiertos = grupos
                    .OrderByDescending(g => g.DiasAntiguedad)
                    .ThenBy(g => g.FechaPrimerApunte)
                    .ToList()
            };
        }

        /// <summary>
        /// Agrupa los apuntes por una clave extraída. Para cada grupo:
        ///   - Si tiene ≥1 DEBE y ≥1 HABER:
        ///       · Salda a 0 → cerrado (no se añade a grupos).
        ///       · No salda → grupo abierto con el saldo residual.
        ///   - Si es solo DEBE o solo HABER → libera los apuntes al residuo.
        /// Los apuntes sin clave (extraerClave devuelve null) siempre pasan al residuo.
        /// </summary>
        private static List<ApunteCuentaDto> ProcesarPasadaPorClave(
            List<ApunteCuentaDto> apuntes,
            Func<ApunteCuentaDto, string> extraerClave,
            TipoClaveGrupo tipoClave,
            DateTime fechaCorte,
            List<GrupoAbiertoDto> gruposDestino)
        {
            var porClave = new Dictionary<string, List<ApunteCuentaDto>>();
            var sinClave = new List<ApunteCuentaDto>();

            foreach (var a in apuntes)
            {
                string clave = extraerClave(a);
                if (string.IsNullOrEmpty(clave))
                {
                    sinClave.Add(a);
                    continue;
                }
                if (!porClave.TryGetValue(clave, out var lista))
                {
                    lista = new List<ApunteCuentaDto>();
                    porClave[clave] = lista;
                }
                lista.Add(a);
            }

            var residual = new List<ApunteCuentaDto>(sinClave);

            foreach (var kvp in porClave)
            {
                var grupo = kvp.Value;
                bool tieneDebe = grupo.Any(a => a.ImporteNeto > 0);
                bool tieneHaber = grupo.Any(a => a.ImporteNeto < 0);

                if (!tieneDebe || !tieneHaber)
                {
                    // Solo DEBE o solo HABER → libera a siguientes pasadas
                    residual.AddRange(grupo);
                    continue;
                }

                decimal saldo = grupo.Sum(a => a.ImporteNeto);
                if (saldo == 0)
                {
                    // Cerrado, no se añade
                    continue;
                }

                var ordenados = grupo.OrderBy(a => a.Fecha).ThenBy(a => a.NumeroOrden).ToList();
                gruposDestino.Add(new GrupoAbiertoDto
                {
                    Clave = kvp.Key,
                    TipoClave = tipoClave,
                    Saldo = saldo,
                    FechaPrimerApunte = ordenados[0].Fecha,
                    DiasAntiguedad = (fechaCorte - ordenados[0].Fecha).Days,
                    Apuntes = ordenados
                });
            }

            return residual;
        }

        /// <summary>
        /// FIFO por fecha ascendente sobre ImporteNeto signed. Cada apunte cancela los de
        /// signo contrario más antiguos. Lo que sobra sale como un grupo por apunte con
        /// TipoClave = Fifo.
        /// </summary>
        private static List<GrupoAbiertoDto> FifoEmparejar(
            List<ApunteCuentaDto> apuntes,
            DateTime fechaCorte)
        {
            var ordenados = apuntes
                .Where(a => a.ImporteNeto != 0)
                .OrderBy(a => a.Fecha).ThenBy(a => a.NumeroOrden)
                .ToList();

            var colaDebe = new LinkedList<ApunteResto>();
            var colaHaber = new LinkedList<ApunteResto>();

            foreach (var a in ordenados)
            {
                decimal neto = a.ImporteNeto;
                decimal resto = Math.Abs(neto);
                var colaContraria = neto > 0 ? colaHaber : colaDebe;

                while (resto > 0 && colaContraria.First != null)
                {
                    var op = colaContraria.First.Value;
                    if (op.Resto <= resto)
                    {
                        resto -= op.Resto;
                        op.Resto = 0;
                        colaContraria.RemoveFirst();
                    }
                    else
                    {
                        op.Resto -= resto;
                        resto = 0;
                    }
                }

                if (resto > 0)
                {
                    var propia = neto > 0 ? colaDebe : colaHaber;
                    propia.AddLast(new ApunteResto { Apunte = a, Resto = resto });
                }
            }

            var abiertos = colaDebe.Concat(colaHaber)
                .OrderBy(x => x.Apunte.Fecha).ThenBy(x => x.Apunte.NumeroOrden)
                .Select(x => new GrupoAbiertoDto
                {
                    Clave = $"FIFO-{x.Apunte.NumeroOrden}",
                    TipoClave = TipoClaveGrupo.Fifo,
                    Saldo = x.Apunte.ImporteNeto > 0 ? x.Resto : -x.Resto,
                    FechaPrimerApunte = x.Apunte.Fecha,
                    DiasAntiguedad = (fechaCorte - x.Apunte.Fecha).Days,
                    Apuntes = new List<ApunteCuentaDto> { x.Apunte }
                })
                .ToList();

            return abiertos;
        }

        private class ApunteResto
        {
            public ApunteCuentaDto Apunte { get; set; }
            public decimal Resto { get; set; }
        }
    }
}
