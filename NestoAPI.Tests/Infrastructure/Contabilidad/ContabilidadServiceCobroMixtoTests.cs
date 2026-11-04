using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Contabilidad;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Contabilidad
{
    /// <summary>
    /// Nesto#561: el cobro de la caja con deudas de la empresa 1 y de la 3 (Nesto#514) se contabilizaba con un solo
    /// prdContabilizar, el de la empresa de la PRIMERA línea. Caso real (Reina, 28/10/26, diario LauraC, cliente 11276):
    /// abono «A CUENTA» de 2,10 de la empresa 1 (los abonos van primero) + GB2601662 de 11,90 de la empresa 3. Se
    /// contabilizó la empresa 1 (asiento 1206811) y las dos líneas de la 3 se quedaron en PreContabilidad; los reintentos
    /// de cobrar la GB2601662 sola liquidaban dos veces el efecto 3058956 («Importes con mismo signo o importe 0»).
    /// </summary>
    [TestClass]
    public class ContabilidadServiceCobroMixtoTests
    {
        private static PreContabilidad Linea(string empresa, string cuenta, decimal debe, decimal haber, int? liquidado = null, string diario = "LauraC")
        {
            return new PreContabilidad
            {
                Empresa = empresa,
                Diario = diario,
                TipoCuenta = liquidado.HasValue ? Constantes.Contabilidad.TiposCuenta.CLIENTE : Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE,
                Nº_Cuenta = cuenta,
                Debe = debe,
                Haber = haber,
                Liquidado = liquidado
            };
        }

        private static List<PreContabilidad> CobroDeReinaDel28DeOctubre()
        {
            return new List<PreContabilidad>
            {
                Linea("1", "11276", 2.10M, 0, 3058952),      // devolución del «a cuenta» (abono, va primero)
                Linea("3", "11276", 0, 11.90M, 3058956),     // S/Pago GB2601662/1
                Linea("1", "57000014", 0, 2.10M),            // caja, empresa 1
                Linea("3", "57000014", 11.90M, 0)            // caja, empresa 3
            };
        }

        private static Task<int> ContabilizarAnotando(List<PreContabilidad> lineas, List<string> llamadas, int asiento)
        {
            return ContabilidadService.ContabilizarPorEmpresaYDiario(lineas, (empresa, diario) =>
            {
                llamadas.Add(empresa + "|" + diario);
                return Task.FromResult(asiento);
            });
        }

        [TestMethod]
        public async Task ContabilizarPorEmpresaYDiario_CobroConDeudasDeLa1YLa3_UnaLlamadaPorEmpresaYDevuelveElAsientoDeLa1()
        {
            var llamadas = new List<string>();

            int asiento = await ContabilidadService.ContabilizarPorEmpresaYDiario(CobroDeReinaDel28DeOctubre(), (empresa, diario) =>
            {
                llamadas.Add(empresa + "|" + diario);
                return Task.FromResult(empresa == "1" ? 1206811 : 164837);
            });

            CollectionAssert.AreEqual(new[] { "1|LauraC", "3|LauraC" }, llamadas, "Antes solo se contabilizaba la empresa de la primera línea (la 1)");
            Assert.AreEqual(1206811, asiento);
        }

        [TestMethod]
        public async Task ContabilizarPorEmpresaYDiario_SoloUnaDeudaDeLa3DesdeLaCajaDeLa1_ContabilizaEnLa3()
        {
            var lineas = new List<PreContabilidad>
            {
                Linea("3", "11276", 0, 11.90M, 3058956),
                Linea("3", "57000014", 11.90M, 0)
            };
            var llamadas = new List<string>();

            int asiento = await ContabilizarAnotando(lineas, llamadas, 164837);

            CollectionAssert.AreEqual(new[] { "3|LauraC" }, llamadas);
            Assert.AreEqual(164837, asiento);
        }

        [TestMethod]
        public async Task ContabilizarPorEmpresaYDiario_EmpresaYDiarioConYSinRelleno_SonLosMismosYSeContabilizanUnaVez()
        {
            // EMPRESA_ESPEJO de Nesto es "3  " (lo «a cuenta») y la de las deudas "3"
            var lineas = new List<PreContabilidad>
            {
                Linea("3", "11276", 0, 11.90M, 3058956),
                Linea("3  ", "11276", 0, 5M),
                Linea("3", "57000014", 16.90M, 0, diario: "LauraC    ")
            };
            var llamadas = new List<string>();

            _ = await ContabilizarAnotando(lineas, llamadas, 1);

            CollectionAssert.AreEqual(new[] { "3|LauraC" }, llamadas);
        }

        [TestMethod]
        public async Task ContabilizarPorEmpresaYDiario_UnaSolaEmpresa_SeComportaComoAntes()
        {
            var lineas = new List<PreContabilidad>
            {
                Linea("1", "11276", 0, 50M, 3000001),
                Linea("1", "57000013", 50M, 0)
            };
            var llamadas = new List<string>();

            int asiento = await ContabilizarAnotando(lineas, llamadas, 777);

            CollectionAssert.AreEqual(new[] { "1|LauraC" }, llamadas);
            Assert.AreEqual(777, asiento);
        }

        [TestMethod]
        public async Task ContabilizarPorEmpresaYDiario_SinLaEmpresaPorDefecto_DevuelveElPrimerAsiento()
        {
            var lineas = new List<PreContabilidad> { Linea("3", "x", 1, 0), Linea("4", "y", 0, 1) };

            int asiento = await ContabilidadService.ContabilizarPorEmpresaYDiario(lineas,
                (empresa, diario) => Task.FromResult(empresa == "3" ? 30 : 40));

            Assert.AreEqual(30, asiento);
        }

        [TestMethod]
        public async Task ContabilizarPorEmpresaYDiario_SiLaSegundaEmpresaNoDaAsiento_DevuelveEseResultadoParaDeshacerTodo()
        {
            // Quien llama (CrearLineasYContabilizarDiarioUnaVez) hace rollback con resultado <= 0: no queda la 1
            // contabilizada y la 3 colgada en PreContabilidad.
            var llamadas = new List<string>();

            int asiento = await ContabilidadService.ContabilizarPorEmpresaYDiario(CobroDeReinaDel28DeOctubre(), (empresa, diario) =>
            {
                llamadas.Add(empresa);
                return Task.FromResult(empresa == "1" ? 1206811 : 0);
            });

            Assert.AreEqual(0, asiento);
            CollectionAssert.AreEqual(new[] { "1", "3" }, llamadas);
        }

        [TestMethod]
        public async Task ContabilizarPorEmpresaYDiario_SiLaSegundaEmpresaFalla_LaExcepcionLlegaAQuienLlama()
        {
            // El SP de la 3 lanza: la excepción sube y CrearLineasYContabilizarDiarioUnaVez deshace la transacción entera.
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
                ContabilidadService.ContabilizarPorEmpresaYDiario(CobroDeReinaDel28DeOctubre(), (empresa, diario) =>
                {
                    if (empresa == "3")
                    {
                        throw new InvalidOperationException("prdContabilizar");
                    }
                    return Task.FromResult(1206811);
                }));
        }

        [TestMethod]
        public void ErroresLiquidaciones_LineaDeLa1ContraUnEfectoDeLa3_DaErrorClaroDeEmpresa()
        {
            var lineas = new List<PreContabilidad> { Linea("1", "11276", 0, 11.90M, 3058956) };
            var destino = new ExtractoCliente { Empresa = "3  ", Nº_Orden = 3058956, Número = "11276     ", ImportePdte = 11.90M };

            List<string> errores = ContabilidadService.ErroresLiquidacionesDiario(lineas, id => destino);

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0], "3058956");
            StringAssert.Contains(errores[0], "empresa 3");
        }

        [TestMethod]
        public void ErroresLiquidaciones_MismaEmpresaConRelleno_NoDaError()
        {
            var lineas = new List<PreContabilidad> { Linea("3", "11276", 0, 11.90M, 3058956) };
            var destino = new ExtractoCliente { Empresa = "3  ", Nº_Orden = 3058956, Número = "11276     ", ImportePdte = 11.90M };

            List<string> errores = ContabilidadService.ErroresLiquidacionesDiario(lineas, id => destino);

            Assert.AreEqual(0, errores.Count, string.Join(" | ", errores));
        }
    }
}
