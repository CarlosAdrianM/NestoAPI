using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Remesas;
using NestoAPI.Models;
using NestoAPI.Models.Remesas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#332 (slices 2-3): crear la remesa. Las piezas puras (validación de la
    /// selección contra candidatos frescos y composición de las líneas de PreContabilidad,
    /// calcadas del asiento real 1195101) se testean aisladas; la orquestación (contador,
    /// alta, contabilizar) es SQL fino sobre el único call site ya probado.
    /// </summary>
    [TestClass]
    public class CrearRemesaServiceTests
    {
        private static EfectoCandidatoDTO Candidato(int id, bool preseleccionado = true,
            string motivo = null, bool conNegativos = false, string cliente = "15191",
            bool forzable = false)
        {
            return new EfectoCandidatoDTO
            {
                Id = id,
                Cliente = cliente,
                Preseleccionado = preseleccionado,
                Motivo = motivo,
                ClienteConNegativos = conNegativos,
                Forzable = forzable
            };
        }

        [TestMethod]
        public void ValidarSeleccion_TodoCandidatoYLimpio_SinErrores()
        {
            var errores = CrearRemesaService.ValidarSeleccion(
                new List<int> { 1, 2 },
                new List<EfectoCandidatoDTO> { Candidato(1), Candidato(2), Candidato(3) });

            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void ValidarSeleccion_EfectoQueYaNoEsCandidato_ErrorDeRefresco()
        {
            // Lección Nesto#397: revalidar con datos FRESCOS en el POST
            var errores = CrearRemesaService.ValidarSeleccion(
                new List<int> { 99 },
                new List<EfectoCandidatoDTO> { Candidato(1) });

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores.Single(), "refresque");
        }

        [TestMethod]
        public void ValidarSeleccion_EfectoRetenidoPorElGating_ErrorConElMotivo()
        {
            var errores = CrearRemesaService.ValidarSeleccion(
                new List<int> { 1 },
                new List<EfectoCandidatoDTO> { Candidato(1, preseleccionado: false,
                    motivo: "Retenido: el pedido tiene envíos de agencia sin confirmar la entrega (#172).") });

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores.Single(), "sin confirmar la entrega");
        }

        [TestMethod]
        public void ValidarSeleccion_ClienteConNegativos_LaPuertaDeNeteoSeRevalidaEnElPost()
        {
            var errores = CrearRemesaService.ValidarSeleccion(
                new List<int> { 1, 2 },
                new List<EfectoCandidatoDTO>
                {
                    Candidato(1, conNegativos: true, cliente: "15191"),
                    Candidato(2, cliente: "30676")
                });

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores.Single(), "15191");
            StringAssert.Contains(errores.Single(), "negativos");
        }

        [TestMethod]
        public void ValidarSeleccion_ClienteConNegativosAceptadoPorElUsuario_SinError()
        {
            // NestoAPI#380: la puerta de neteo deja de ser OBLIGATORIA. Caso real: un pago a
            // cuenta de -30 € de un curso de septiembre no debe impedir girar un efecto de
            // 450 € que no tiene nada que ver — el usuario confirma el aviso y se remesa.
            var errores = CrearRemesaService.ValidarSeleccion(
                new List<int> { 1, 2 },
                new List<EfectoCandidatoDTO>
                {
                    Candidato(1, conNegativos: true, cliente: "15191"),
                    Candidato(2, cliente: "30676")
                },
                aceptarClientesConNegativos: true);

            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void ValidarSeleccion_AceptarNegativosNoAbreLasDemasPuertas_SigueBloqueando()
        {
            // El flag SOLO relaja la puerta de neteo: un efecto retenido (gating, IBAN #381)
            // sigue bloqueando aunque el usuario haya aceptado los negativos.
            var errores = CrearRemesaService.ValidarSeleccion(
                new List<int> { 1 },
                new List<EfectoCandidatoDTO> { Candidato(1, preseleccionado: false, conNegativos: true,
                    motivo: "Retenido: el IBAN de la ficha bancaria está incompleto (#381).") },
                aceptarClientesConNegativos: true);

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores.Single(), "IBAN");
        }

        // Fallo 20/08/26 (caso real 3028653): el gating de entrega se puede FORZAR por efecto —
        // el usuario confirma que quiere remesarlo aunque el envío no conste entregado. Solo
        // las retenciones Forzables (entrega pendiente/incidentado); IBAN, estado bloqueado y
        // DEVUELTO siguen bloqueando aunque se intenten forzar.

        [TestMethod]
        public void ValidarSeleccion_EfectoForzableForzadoPorElUsuario_SinError()
        {
            var errores = CrearRemesaService.ValidarSeleccion(
                new List<int> { 1 },
                new List<EfectoCandidatoDTO> { Candidato(1, preseleccionado: false, forzable: true,
                    motivo: "Retenido: el pedido tiene envíos de agencia sin confirmar la entrega (#172).") },
                efectosForzados: new List<int> { 1 });

            Assert.AreEqual(0, errores.Count);
        }

        [TestMethod]
        public void ValidarSeleccion_EfectoForzableSinForzar_SigueRetenido()
        {
            var errores = CrearRemesaService.ValidarSeleccion(
                new List<int> { 1 },
                new List<EfectoCandidatoDTO> { Candidato(1, preseleccionado: false, forzable: true,
                    motivo: "Retenido: el pedido tiene envíos de agencia sin confirmar la entrega (#172).") });

            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public void ValidarSeleccion_EfectoNoForzableAunqueSeFuerce_SigueBloqueando()
        {
            // Forzar solo abre el gating de entrega: el IBAN roto (#381) o el DEVUELTO no
            var errores = CrearRemesaService.ValidarSeleccion(
                new List<int> { 1 },
                new List<EfectoCandidatoDTO> { Candidato(1, preseleccionado: false, forzable: false,
                    motivo: "Retenido: el IBAN de la ficha bancaria está incompleto (#381).") },
                efectosForzados: new List<int> { 1 });

            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores.Single(), "IBAN");
        }

        // Composición de líneas: calcada del asiento real 1195101 (remesa 10898, 20/07/26)

        private static ExtractoCliente Efecto(int orden, string cliente, decimal pendiente,
            string documento, string efecto = "1", DateTime? vencimiento = null)
        {
            return new ExtractoCliente
            {
                Empresa = "1",
                Nº_Orden = orden,
                Número = cliente,
                Contacto = "0",
                ImportePdte = pendiente,
                Nº_Documento = documento,
                Efecto = efecto,
                FormaPago = "RCB",
                FormaVenta = "VAR",
                Delegación = "ALG",
                Vendedor = "NV",
                CCC = "1",
                FechaVto = vencimiento
            };
        }

        private static Banco BancoSabadell() => new Banco
        {
            Empresa = "1",
            Número = "5",
            Cuenta_Contable = "57200013"
        };

        [TestMethod]
        public void ConstruirLineasRemesa_CuadraDebeYHaberYLiquidaCadaEfecto()
        {
            var efectos = new List<ExtractoCliente>
            {
                Efecto(3016000, "40227", 14.65m, "NV2608461", "2"),
                Efecto(3019661, "40185", 326.77m, "NV2609151")
            };

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), efectos, "NUEVAVISION\\Carlos");

            Assert.AreEqual(3, lineas.Count, "Una línea por efecto + la del banco");
            Assert.AreEqual(lineas.Sum(l => l.Debe), lineas.Sum(l => l.Haber), "El asiento debe cuadrar");

            PreContabilidad banco = lineas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE);
            Assert.AreEqual("57200013", banco.Nº_Cuenta);
            Assert.AreEqual(341.42m, banco.Debe);
            StringAssert.Contains(banco.Concepto, "Remesa:10900");
            Assert.AreEqual("10900", banco.Nº_Documento);

            PreContabilidad pago = lineas.Single(l => l.Liquidado == 3016000);
            Assert.AreEqual("40227", pago.Nº_Cuenta);
            Assert.AreEqual(14.65m, pago.Haber);
            Assert.AreEqual(Constantes.TiposExtractoCliente.PAGO, pago.TipoApunte);
            Assert.AreEqual("10900", pago.Nº_Remesa, "El pago nace con la remesa: prdLiquidar la propaga a la cartera");
            StringAssert.Contains(pago.Concepto, "Pago Factura NV2608461");
            Assert.AreEqual(CrearRemesaService.DIARIO_REMESA, pago.Diario);
        }

        [TestMethod]
        public void ConstruirLineasRemesa_TodasLasLineasLlevanRemesaDiarioYUsuario()
        {
            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), new List<ExtractoCliente> { Efecto(1, "15191", 100m, "NV1") }, "carlos");

            Assert.IsTrue(lineas.All(l => l.Nº_Remesa == "10900"));
            Assert.IsTrue(lineas.All(l => l.Diario == CrearRemesaService.DIARIO_REMESA));
            Assert.IsTrue(lineas.All(l => l.Usuario == "carlos"));
            Assert.IsTrue(lineas.All(l => l.Asiento_Automático));
        }

        // Fallo 20/08/26 (apunte real 7100551): el apunte del BANCO iba sin delegación ni forma
        // de venta (los de pago del cliente sí las llevan, de su efecto). Se rellena con la
        // mayoritaria entre los efectos del grupo; sin datos, con las de por defecto.

        [TestMethod]
        public void ConstruirLineasRemesa_LaLineaDelBancoLlevaDelegacionYFormaVentaMayoritarias()
        {
            var conReina = Efecto(1, "15191", 100m, "NV1");
            conReina.Delegación = "REI";
            conReina.FormaVenta = "TEL";
            var efectos = new List<ExtractoCliente>
            {
                conReina,
                Efecto(2, "30676", 50m, "NV2"),   // ALG / VAR
                Efecto(3, "40227", 25m, "NV3")    // ALG / VAR
            };

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), efectos, "carlos");

            PreContabilidad banco = lineas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE);
            Assert.AreEqual("ALG", banco.Delegación, "La delegación mayoritaria entre los efectos (2 ALG vs 1 REI)");
            Assert.AreEqual("VAR", banco.FormaVenta, "La forma de venta mayoritaria (2 VAR vs 1 TEL)");
        }

        [TestMethod]
        public void ConstruirLineasRemesa_EfectosSinDelegacionNiFormaVenta_ElBancoLlevaLasPorDefecto()
        {
            var sinDatos = Efecto(1, "15191", 100m, "NV1");
            sinDatos.Delegación = null;
            sinDatos.FormaVenta = "  ";

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), new List<ExtractoCliente> { sinDatos }, "carlos");

            PreContabilidad banco = lineas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE);
            Assert.AreEqual(Constantes.Empresas.DELEGACION_POR_DEFECTO, banco.Delegación);
            Assert.AreEqual(Constantes.Empresas.FORMA_VENTA_POR_DEFECTO, banco.FormaVenta);
        }

        // NestoAPI#480 (casos reales 10930, 10931 y 10937): la contabilización agrupaba por
        // (secuencia SEPA, fecha) desde #386, pero el fichero se genera siempre dos veces y el SP
        // volteaba los FRST a RCUR entre medias, así que el banco abonaba 2 bloques contra 3
        // asientos. Desde el Rulebook SEPA de 2016 la secuencia es informativa: todo viaja como
        // RCUR y se agrupa SOLO por fecha, igual que el fichero (prdCrearRemesaIso20022 con
        // Scripts/Issue480_prdCrearRemesaIso20022_TodoRCUR.sql).

        [TestMethod]
        public void ConstruirLineasRemesa_MismaFecha_UnSoloAsientoAunqueHubieraUnMandatoNuevo()
        {
            // Antes (#386) el 41223 con ficha FRST iba a su propio asiento (327,39) y el banco
            // abonó 1.081,55 de una vez: 3 asientos contra 2 abonos en la 10930.
            var efectos = new List<ExtractoCliente>
            {
                Efecto(1, "41223", 327.39m, "NV1"),
                Efecto(2, "13767", 161.14m, "NV2"),
                Efecto(3, "25431", 95.14m, "NV3")
            };

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10930, "1", BancoSabadell(), efectos, "carlos");

            List<PreContabilidad> bancos = lineas
                .Where(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE).ToList();
            Assert.AreEqual(1, bancos.Count, "Un solo abono por fecha, como hace el banco con todo RCUR");
            Assert.AreEqual(583.67m, bancos[0].Debe);
            Assert.IsFalse(bancos[0].Concepto.Contains("FRST"));
            Assert.AreEqual(1, lineas.Select(l => l.Asiento).Distinct().Count(), "Todos en el mismo asiento");
            Assert.AreEqual(lineas.Sum(l => l.Debe), lineas.Sum(l => l.Haber), "El asiento debe cuadrar");
        }

        [TestMethod]
        public void ConstruirLineasRemesa_DosFechasDeCargo_DosAsientosSinDistinguirSecuencia()
        {
            // Réplica de la 10908 con la regla nueva: 2 fechas → 2 abonos (2.255,71 + 447,01).
            DateTime hoy = DateTime.Today;
            var efectos = new List<ExtractoCliente>
            {
                Efecto(1, "15191", 131.62m, "NV1", vencimiento: hoy.AddDays(1)),
                Efecto(2, "26985", 2124.09m, "NV2", vencimiento: hoy.AddDays(1)),
                Efecto(3, "40227", 447.01m, "NV3", vencimiento: hoy.AddDays(2))
            };

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10908, "1", BancoSabadell(), efectos, "carlos", respetarVencimientos: true);

            List<PreContabilidad> bancos = lineas
                .Where(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE)
                .OrderBy(l => l.Asiento).ToList();
            Assert.AreEqual(2, bancos.Count, "Un abono por fecha de cargo");
            Assert.AreEqual(2255.71m, bancos[0].Debe);
            Assert.AreEqual(447.01m, bancos[1].Debe);
            Assert.AreEqual(2, bancos.Select(b => b.Asiento).Distinct().Count());
            Assert.AreEqual(lineas.Sum(l => l.Debe), lineas.Sum(l => l.Haber));
        }

        [TestMethod]
        public void ConstruirLineasRemesa_ElDiaDeValorEsElMismoParaTodosLosDelGrupo()
        {
            // Con todo RCUR desaparece la excepción de #386 (los FRST se abonaban el día de la
            // presentación): el grupo de hoy sube entero a la próxima fecha de cargo.
            DateTime hoy = DateTime.Today;
            DateTime lunes = hoy.AddDays(3);
            var efectos = new List<ExtractoCliente>
            {
                Efecto(1, "15191", 257.06m, "NV1", vencimiento: hoy),
                Efecto(2, "26985", 2141.50m, "NV2", vencimiento: hoy)
            };

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10909, "1", BancoSabadell(), efectos, "carlos", respetarVencimientos: true,
                fechaValorMinima: lunes);

            PreContabilidad banco = lineas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE);
            Assert.AreEqual(lunes, banco.Fecha, "El abono sube al siguiente laborable (D-1 de SEPA)");
            Assert.IsTrue(lineas.All(l => l.Fecha == lunes), "Los pagos acompañan a su banco");
            Assert.AreEqual(2398.56m, banco.Debe);
        }

        [TestMethod]
        public void ConstruirLineasRemesa_SinSecuencias_UnGrupoPorFechaYConceptoSinSufijo()
        {
            var efectos = new List<ExtractoCliente>
            {
                Efecto(1, "15191", 100m, "NV1"),
                Efecto(2, "26985", 50m, "NV2")
            };

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), efectos, "carlos");

            PreContabilidad banco = lineas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE);
            Assert.AreEqual(150m, banco.Debe);
            Assert.IsFalse(banco.Concepto.Contains("FRST"));
        }

        // NestoAPI#345: remesa multi-fecha (viernes cubre sábado y domingo; vísperas de festivo)

        [TestMethod]
        public void ConstruirLineasRemesa_RespetandoVencimientos_UnaLineaDeBancoPorFecha()
        {
            DateTime hoy = DateTime.Today;
            var efectos = new List<ExtractoCliente>
            {
                Efecto(1, "15191", 100m, "NV1", vencimiento: hoy.AddDays(1)),
                Efecto(2, "26985", 50m, "NV2", vencimiento: hoy.AddDays(1)),
                Efecto(3, "40227", 25m, "NV3", vencimiento: hoy.AddDays(2))
            };

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), efectos, "carlos", respetarVencimientos: true);

            List<PreContabilidad> bancos = lineas
                .Where(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE)
                .OrderBy(l => l.Fecha).ToList();
            Assert.AreEqual(2, bancos.Count, "Un apunte de banco POR FECHA de cargo");
            Assert.AreEqual(hoy.AddDays(1), bancos[0].Fecha);
            Assert.AreEqual(150m, bancos[0].Debe);
            Assert.AreEqual(hoy.AddDays(2), bancos[1].Fecha);
            Assert.AreEqual(25m, bancos[1].Debe);
            Assert.AreEqual(lineas.Sum(l => l.Debe), lineas.Sum(l => l.Haber), "El asiento debe cuadrar");

            PreContabilidad pagoLunes = lineas.Single(l => l.Liquidado == 3);
            Assert.AreEqual(hoy.AddDays(2), pagoLunes.Fecha, "El pago lleva la fecha de SU vencimiento");
            Assert.AreEqual(hoy.AddDays(2), pagoLunes.FechaVto);
        }

        [TestMethod]
        public void ConstruirLineasRemesa_RespetandoVencimientos_CadaFechaEsUnAsientoDistinto()
        {
            // Bug 23/07/26: todas las líneas iban con Asiento = 1 y prdContabilizar abortaba
            // con "El Asiento 1 tiene diferentes fechas". Cada fecha de cargo debe formar su
            // propio asiento provisional (efectos + banco) y cuadrar por sí mismo.
            DateTime hoy = DateTime.Today;
            var efectos = new List<ExtractoCliente>
            {
                Efecto(1, "15191", 100m, "NV1", vencimiento: hoy.AddDays(-1)),
                Efecto(2, "26985", 50m, "NV2", vencimiento: hoy),
                Efecto(3, "40227", 25m, "NV3", vencimiento: hoy.AddDays(1))
            };

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), efectos, "carlos", respetarVencimientos: true);

            foreach (var asiento in lineas.GroupBy(l => l.Asiento))
            {
                Assert.AreEqual(1, asiento.Select(l => l.Fecha).Distinct().Count(),
                    $"El asiento {asiento.Key} tiene diferentes fechas: prdContabilizar lo rechaza");
                Assert.AreEqual(asiento.Sum(l => l.Debe), asiento.Sum(l => l.Haber),
                    $"El asiento {asiento.Key} debe cuadrar por sí mismo");
            }
            Assert.AreEqual(2, lineas.Select(l => l.Asiento).Distinct().Count(),
                "Dos fechas de cargo (hoy y mañana) = dos asientos provisionales");
            Assert.IsTrue(lineas.Where(l => l.Fecha == hoy).All(l => l.Asiento == 1),
                "La primera fecha va al asiento 1");
            Assert.IsTrue(lineas.Where(l => l.Fecha == hoy.AddDays(1)).All(l => l.Asiento == 2),
                "La segunda fecha va al asiento 2");
        }

        [TestMethod]
        public void ConstruirLineasRemesa_RespetandoVencimientos_ElGrupoDelDiaSeValoraElSiguienteLaborablePeroSeparado()
        {
            // NestoAPI#345 (ajuste 24/07/26): La Caixa valoró los recibos con cargo HOY el día
            // SIGUIENTE (SEPA D-1: no se cobra el mismo día de la presentación), pero en un apunte
            // SEPARADO del grupo de mañana (no los funde). Remesa real 10901: 2 efectos del 23
            // (69,93 €) + 1 del 24 (16,63 €) -> el banco hizo DOS abonos, AMBOS con fecha 24.
            DateTime hoy = DateTime.Today;
            DateTime manana = hoy.AddDays(1);
            var efectos = new List<ExtractoCliente>
            {
                Efecto(1, "15191", 40.66m, "NV1", vencimiento: hoy),
                Efecto(2, "26985", 29.27m, "NV2", vencimiento: hoy),
                Efecto(3, "40227", 16.63m, "NV3", vencimiento: manana)
            };

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10901, "1", BancoSabadell(), efectos, "carlos",
                respetarVencimientos: true, fechaCargo: hoy, fechaValorMinima: manana);

            // Dos apuntes de banco SEPARADOS (dos asientos), pero AMBOS con fecha de valor = mañana
            List<PreContabilidad> bancos = lineas
                .Where(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE)
                .ToList();
            Assert.AreEqual(2, bancos.Count, "El banco los mantiene separados: un apunte por fecha solicitada");
            Assert.IsTrue(bancos.All(b => b.Fecha == manana), "Ambos apuntes van con fecha del día siguiente (día de valor)");
            Assert.AreEqual(2, bancos.Select(b => b.Asiento).Distinct().Count(), "Siguen siendo dos asientos distintos");
            CollectionAssert.AreEquivalent(new[] { 69.93m, 16.63m }, bancos.Select(b => b.Debe).ToList(),
                "Cada grupo conserva su importe: no se funden en 86,56");

            // Todo se contabiliza en el día de valor (mañana), pero cada asiento cuadra por sí mismo
            Assert.IsTrue(lineas.All(l => l.Fecha == manana), "Nada se contabiliza en la fecha de presentación (hoy)");
            foreach (var asiento in lineas.GroupBy(l => l.Asiento))
            {
                Assert.AreEqual(1, asiento.Select(l => l.Fecha).Distinct().Count(),
                    $"El asiento {asiento.Key} tiene una única fecha (prdContabilizar lo exige)");
                Assert.AreEqual(asiento.Sum(l => l.Debe), asiento.Sum(l => l.Haber), $"El asiento {asiento.Key} cuadra");
            }

            // La ReqdColltnDt del fichero SEPA sale de FechaVto: los recibos del día se siguen
            // SOLICITANDO al día de hoy (dos PmtInf, 23 y 24), que es lo que produce los dos abonos.
            PreContabilidad pagoHoy = lineas.Single(l => l.Liquidado == 1);
            Assert.AreEqual(hoy, pagoHoy.FechaVto, "El fichero SEPA sigue pidiendo el cargo al día solicitado (hoy)");
            Assert.AreEqual(manana, pagoHoy.Fecha, "pero contablemente se valora el día siguiente");
        }

        [TestMethod]
        public void ConstruirLineasRemesa_VencimientoPasadoONulo_SeCobraEnLaFechaDeCargo()
        {
            // "Hay que controlar que las fechas nunca sean anteriores a hoy" (Carlos 22/07)
            DateTime hoy = DateTime.Today;
            var efectos = new List<ExtractoCliente>
            {
                Efecto(1, "15191", 100m, "NV1", vencimiento: hoy.AddDays(-10)),
                Efecto(2, "26985", 50m, "NV2", vencimiento: null)
            };

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), efectos, "carlos", respetarVencimientos: true);

            Assert.IsTrue(lineas.All(l => l.Fecha == hoy), "Nada puede ir con fecha anterior a hoy");
            Assert.AreEqual(1, lineas.Count(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE),
                "Todos caen en la misma fecha: un solo apunte de banco");
        }

        [TestMethod]
        public void ConstruirLineasRemesa_ForzandoFechaDeCargo_TodoVaAEsaFecha()
        {
            // Modo forzado (default): vísperas de festivo — remesa de hoy con fecha de mañana
            DateTime manana = DateTime.Today.AddDays(1);
            var efectos = new List<ExtractoCliente>
            {
                Efecto(1, "15191", 100m, "NV1", vencimiento: DateTime.Today.AddDays(5)),
                Efecto(2, "26985", 50m, "NV2", vencimiento: DateTime.Today)
            };

            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), efectos, "carlos", respetarVencimientos: false, fechaCargo: manana);

            Assert.IsTrue(lineas.All(l => l.Fecha == manana && l.FechaVto == manana),
                "En modo forzado TODOS los efectos van a la fecha de cargo, ignorando su vencimiento");
            Assert.AreEqual(1, lineas.Count(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE));
        }

        [TestMethod]
        public void FechaCargoEfectiva_NuncaAnteriorAHoy()
        {
            Assert.AreEqual(DateTime.Today, CrearRemesaService.FechaCargoEfectiva(null));
            Assert.AreEqual(DateTime.Today, CrearRemesaService.FechaCargoEfectiva(DateTime.Today.AddDays(-3)));
            Assert.AreEqual(DateTime.Today.AddDays(2), CrearRemesaService.FechaCargoEfectiva(DateTime.Today.AddDays(2)));
        }

        [TestMethod]
        public void ProximaFechaCargo_SaltaFinesDeSemanaYFestivos()
        {
            // Solo fines de semana como no laborables para el test
            Func<DateTime, bool> finde = f => f.DayOfWeek == DayOfWeek.Saturday || f.DayOfWeek == DayOfWeek.Sunday;
            var jueves = new DateTime(2026, 7, 23);
            var viernes = new DateTime(2026, 7, 24);

            Assert.AreEqual(viernes, CrearRemesaService.ProximaFechaCargo(jueves, 1, finde), "Jueves + 1 = viernes");
            Assert.AreEqual(new DateTime(2026, 7, 27), CrearRemesaService.ProximaFechaCargo(viernes, 1, finde),
                "Viernes + 1 cae en sábado → salta al LUNES (mejor que el domingo)");

            // Festivo: el viernes 24 es festivo → jueves + 1 salta al lunes
            Func<DateTime, bool> findeYViernesFestivo = f => finde(f) || f == viernes;
            Assert.AreEqual(new DateTime(2026, 7, 27), CrearRemesaService.ProximaFechaCargo(jueves, 1, findeYViernesFestivo));

            // Antelación configurable (parámetro DiasAntelacionRemesa): estilo eléctricas, 5 días
            Assert.AreEqual(new DateTime(2026, 7, 28), CrearRemesaService.ProximaFechaCargo(jueves, 5, finde),
                "Jueves + 5 = martes siguiente (el 28); si cayera en finde saltaría");
        }

        [TestMethod]
        public void ConstruirLineasRemesa_LaLineaDelBancoLlevaTipoApunte()
        {
            // Bug 22/07/26 (2º intento en vivo): PreContabilidad.TipoApunte es NOT NULL y la
            // línea del banco no lo rellenaba → DbEntityValidationException al guardar y remesa
            // abortada. El asiento real de la remesa 10898 lleva TipoApunte "1" en el banco.
            List<PreContabilidad> lineas = CrearRemesaService.ConstruirLineasRemesa(
                10900, "1", BancoSabadell(), new List<ExtractoCliente> { Efecto(1, "15191", 100m, "NV1") }, "carlos");

            PreContabilidad banco = lineas.Single(l => l.TipoCuenta == Constantes.Contabilidad.TiposCuenta.CUENTA_CONTABLE);
            Assert.AreEqual(Constantes.TiposExtractoCliente.FACTURA, banco.TipoApunte);
            Assert.IsTrue(lineas.All(l => !string.IsNullOrEmpty(l.TipoApunte)),
                "Ninguna línea puede ir sin TipoApunte (columna NOT NULL)");
        }

        // Controller (regla de la casa)

        [TestMethod]
        public async Task CrearRemesa_ConExito_DevuelveLaRespuesta()
        {
            IRemesasService servicio = A.Fake<IRemesasService>();
            _ = A.CallTo(() => servicio.CrearRemesaAsync(A<CrearRemesaRequest>.Ignored, A<string>.Ignored))
                .Returns(new CrearRemesaResponse { NumeroRemesa = 10900, Importe = 341.42m, NumeroEfectos = 2 });
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.CrearRemesa(new CrearRemesaRequest
            { Empresa = "1", Banco = "5", Efectos = new List<int> { 1, 2 } })
                as OkNegotiatedContentResult<CrearRemesaResponse>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(10900, resultado.Content.NumeroRemesa);
        }

        [TestMethod]
        public async Task CrearRemesa_ConValidacionFallida_BadRequestConElMotivo()
        {
            IRemesasService servicio = A.Fake<IRemesasService>();
            _ = A.CallTo(() => servicio.CrearRemesaAsync(A<CrearRemesaRequest>.Ignored, A<string>.Ignored))
                .Throws(new System.InvalidOperationException("El efecto 99 ya no es candidato a remesa"));
            RemesasController controller = new RemesasController(servicio);

            var resultado = await controller.CrearRemesa(new CrearRemesaRequest
            { Empresa = "1", Banco = "5", Efectos = new List<int> { 99 } }) as BadRequestErrorMessageResult;

            Assert.IsNotNull(resultado);
            StringAssert.Contains(resultado.Message, "ya no es candidato");
        }
    }
}
