using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Models;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// Nesto#340 (Agencias, slice A4.1) — PARIDAD DE CAMPOS con el cliente antes de migrar, según el
    /// protocolo de pies de plomo acordado el 20/08/26 tras los 3 sustos de A2.
    ///
    /// Estos tests fijan, campo a campo, el apunte de PreContabilidad que construía
    /// <c>AgenciaService.ContabilizarReembolso</c> en Nesto (VB.NET). Una sola diferencia aquí
    /// descuadra la contabilidad de reembolsos, y el error no aparecería hasta el cuadre de banco.
    /// </summary>
    [TestClass]
    public class TramitacionEnviosServiceTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 8, 25);
        private const string USUARIO = "NUEVAVISION\\Alfredo";

        private static Empresa EmpresaConVarios() => new Empresa
        {
            Número = "1  ",
            FormaPagoEfectivo = "EFC",
            DelegaciónVarios = "ALG",
            FormaVentaVarios = "DIR"
        };

        private static EnviosAgencia EnvioConReembolso(decimal reembolso = 121.50M) => new EnviosAgencia
        {
            Numero = 247975,
            Empresa = "1  ",
            Cliente = "15191     ",
            Contacto = "0  ",
            Pedido = 922175,
            Vendedor = "NV ",
            Reembolso = reembolso,
            Empresa1 = EmpresaConVarios(),
            AgenciasTransporte = new AgenciaTransporte { Numero = 12, Nombre = "Innovatrans", CuentaReembolsos = "5720000001 " }
        };

        private static ExtractoCliente MovimientoPendiente(decimal importePdte, DateTime? fecha = null, int orden = 5001) =>
            new ExtractoCliente
            {
                Nº_Orden = orden,
                Nº_Documento = "NV2612946",
                Delegación = "ALC",
                FormaVenta = "TEL",
                Ruta = "00 ",
                Efecto = "1",
                ImportePdte = importePdte,
                Fecha = fecha ?? HOY
            };

        [TestMethod]
        public void ConstruirApunteReembolso_SinMovimientoALiquidar_CalcaLosCamposDelCliente()
        {
            EnviosAgencia envio = EnvioConReembolso();

            PreContabilidad linea = TramitacionEnviosService.ConstruirApunteReembolso(envio, null, HOY, USUARIO);

            Assert.AreEqual("1", linea.Empresa, "La empresa va trimeada");
            Assert.AreEqual("_Reembolso", linea.Diario);
            Assert.AreEqual("3", linea.TipoApunte, "Pago");
            Assert.AreEqual("2", linea.TipoCuenta, "Cliente");
            Assert.AreEqual("15191", linea.Nº_Cuenta);
            Assert.AreEqual("0", linea.Contacto);
            Assert.AreEqual(HOY, linea.Fecha);
            Assert.AreEqual(HOY, linea.FechaVto);
            Assert.AreEqual(121.50M, linea.Haber);
            Assert.AreEqual(0M, linea.Debe);
            Assert.AreEqual("5720000001", linea.Contrapartida, "La cuenta de reembolsos va trimeada");
            Assert.IsFalse(linea.Asiento_Automático);
            Assert.AreEqual("EFC", linea.FormaPago, "La forma de pago en efectivo de la empresa");
            Assert.AreEqual("NV ", linea.Vendedor, "El vendedor va SIN trimear, como en el cliente");
            // Sin movimiento que liquidar: el pago va contra el pedido, con los datos "varios".
            Assert.AreEqual("922175", linea.Nº_Documento);
            Assert.AreEqual("ALG", linea.Delegación);
            Assert.AreEqual("DIR", linea.FormaVenta);
            Assert.IsNull(linea.Liquidado);
            Assert.IsNull(linea.Ruta);
            Assert.IsNull(linea.Efecto);
        }

        /// <summary>
        /// Regresión del 31/08/2026, primer día del piloto de tramitación por API: el apunte se
        /// construía SIN Usuario. `PreContabilidad.Usuario` es NOT NULL con DEFAULT suser_sname(),
        /// y desde Nesto viejo lo rellenaba SQL Server; por la API, EF6 valida ANTES de enviar, ve
        /// el campo obligatorio vacío y lanza "El campo Usuario es obligatorio". La sentencia no
        /// llegaba nunca al servidor, así que el DEFAULT no tenía ocasión de actuar.
        ///
        /// Resultado en producción: la agencia aceptaba el envío (ASM devolvía OK) pero el
        /// reembolso no se contabilizaba y el envío se quedaba ABIERTO. Tres envíos de Alfredo,
        /// los tres únicos con reembolso de las 44 tramitaciones de aquel día.
        /// </summary>
        [TestMethod]
        public void ConstruirApunteReembolso_LlevaSiempreUsuario_PorqueEFValidaAntesDeQueActueElDefault()
        {
            PreContabilidad linea = TramitacionEnviosService.ConstruirApunteReembolso(
                EnvioConReembolso(), null, HOY, USUARIO);

            Assert.AreEqual(USUARIO, linea.Usuario,
                "Sin Usuario, EF rechaza el apunte y el envio se queda abierto con la agencia ya avisada");

            // La misma trampa, y la que salio DESPUES de arreglar la primera: sin fecha, EF manda
            // 01/01/0001 y SQL Server no puede convertir ese datetime2 a datetime (minimo 1753).
            Assert.AreNotEqual(default(DateTime), linea.Fecha_Modificación,
                "Sin Fecha Modificacion, SQL Server rechaza el apunte por fecha fuera de intervalo");
        }

        [TestMethod]
        public void ConstruirApunteReembolso_ConMovimientoALiquidar_TomaSusDatosYNoLosDeVarios()
        {
            EnviosAgencia envio = EnvioConReembolso();
            ExtractoCliente movimiento = MovimientoPendiente(121.50M);

            PreContabilidad linea = TramitacionEnviosService.ConstruirApunteReembolso(envio, movimiento, HOY, USUARIO);

            Assert.AreEqual("NV2612946", linea.Nº_Documento, "El documento es el del movimiento, no el pedido");
            Assert.AreEqual(5001, linea.Liquidado);
            Assert.AreEqual("ALC", linea.Delegación, "La delegación es la del movimiento, no la de varios");
            Assert.AreEqual("TEL", linea.FormaVenta);
            Assert.AreEqual("00 ", linea.Ruta);
            Assert.AreEqual("1", linea.Efecto);
        }

        [TestMethod]
        public void GenerarConcepto_ComponeElTextoDelCliente()
        {
            PreContabilidad linea = TramitacionEnviosService.ConstruirApunteReembolso(EnvioConReembolso(), null, HOY, USUARIO);

            Assert.AreEqual("S/Pago pedido 922175 a Innovatrans c/15191", linea.Concepto);
        }

        [TestMethod]
        public void GenerarConcepto_TextoLargo_SeRecortaA50()
        {
            // La columna Concepto admite 50: el cliente hacía Left(..., 50) y aquí hay que hacer lo
            // mismo o el apunte revienta al guardar.
            EnviosAgencia envio = EnvioConReembolso();
            envio.AgenciasTransporte.Nombre = "Agencia con un nombre larguísimo de transportes urgentes";

            string concepto = TramitacionEnviosService.GenerarConcepto(envio);

            Assert.AreEqual(50, concepto.Length);
            StringAssert.StartsWith(concepto, "S/Pago pedido 922175 a Agencia con un nombre");
        }

        // ===== Elección del movimiento a liquidar =====

        [TestMethod]
        public void ElegirMovimientoLiq_SinMovimientos_DevuelveNulo()
        {
            Assert.IsNull(TramitacionEnviosService.ElegirMovimientoLiq(new List<ExtractoCliente>(), 121.50M, HOY));
            Assert.IsNull(TramitacionEnviosService.ElegirMovimientoLiq(null, 121.50M, HOY));
        }

        [TestMethod]
        public void ElegirMovimientoLiq_UnSoloMovimiento_LoDevuelveAunqueElImporteNoCuadre()
        {
            List<ExtractoCliente> movimientos = new List<ExtractoCliente> { MovimientoPendiente(99M) };

            ExtractoCliente elegido = TramitacionEnviosService.ElegirMovimientoLiq(movimientos, 121.50M, HOY);

            Assert.AreEqual(99M, elegido.ImportePdte, "Con uno solo el cliente no comparaba importes");
        }

        [TestMethod]
        public void ElegirMovimientoLiq_VariosConImporteExacto_DevuelveElUltimoQueCuadra()
        {
            List<ExtractoCliente> movimientos = new List<ExtractoCliente>
            {
                MovimientoPendiente(50M, orden: 1),
                MovimientoPendiente(121.50M, orden: 2),
                MovimientoPendiente(121.50M, orden: 3),
                MovimientoPendiente(80M, orden: 4)
            };

            ExtractoCliente elegido = TramitacionEnviosService.ElegirMovimientoLiq(movimientos, 121.50M, HOY);

            Assert.AreEqual(3, elegido.Nº_Orden, "De los que cuadran, el último");
        }

        [TestMethod]
        public void ElegirMovimientoLiq_VariosSinImporteExacto_DevuelveElUltimoDeTodos()
        {
            List<ExtractoCliente> movimientos = new List<ExtractoCliente>
            {
                MovimientoPendiente(50M, orden: 1),
                MovimientoPendiente(80M, orden: 2)
            };

            ExtractoCliente elegido = TramitacionEnviosService.ElegirMovimientoLiq(movimientos, 121.50M, HOY);

            Assert.AreEqual(2, elegido.Nº_Orden);
        }

        [TestMethod]
        public void ElegirMovimientoLiq_ReembolsoNegativo_ExigeAdemasQueSeaDeHoy()
        {
            // Con reembolso negativo (devolución) el cliente filtraba también por fecha de HOY: con
            // la fecha del envío fallaba cuando la etiqueta era del día anterior.
            List<ExtractoCliente> movimientos = new List<ExtractoCliente>
            {
                MovimientoPendiente(-30M, fecha: HOY.AddDays(-1), orden: 1),
                MovimientoPendiente(-30M, fecha: HOY, orden: 2)
            };

            ExtractoCliente elegido = TramitacionEnviosService.ElegirMovimientoLiq(movimientos, -30M, HOY);

            Assert.AreEqual(2, elegido.Nº_Orden, "El de ayer no vale aunque el importe cuadre");
        }
    }
}
