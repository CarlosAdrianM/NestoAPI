using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#718: pedidos 928370 (cliente 25892) y 929262 (cliente 20459). La plantilla los modificó con «sin IVA» (PUT con
    /// iva null sobre una cabecera G21): el PUT dejó a 0 % las líneas que ya tenían y creó las nuevas al 21 % porque
    /// CrearLineaVta releía la cabecera de la BD (otro contexto, todavía G21). Al facturar, el traspaso a Global (empresa 3,
    /// todo al 0 %) las dejó todas sin IVA, pero la etiqueta ya había salido con la suma de las líneas:
    /// 928370 → 182,44 en la etiqueta y 175,93 en la factura GB2601696; 929262 → 440,18 y 450,05 (GB2601695).
    /// </summary>
    [TestClass]
    public class Issue718IvaLineasNuevasGlobalTests
    {
        private const string EMPRESA = "1";

        // ------------------------------------------------------------ servicio

        private static IServicioPedidosVenta Servicio(int pedido, string ivaCabeceraEnBd = "G21")
        {
            var servicio = A.Fake<IServicioPedidosVenta>();
            A.CallTo(() => servicio.LeerCabPedidoVta(A<string>._, pedido)).Returns(new CabPedidoVta
            {
                Empresa = "1  ",
                Número = pedido,
                IVA = ivaCabeceraEnBd,
                PlazosPago = "CONTADO",
                Forma_Pago = "EFC",
                Nº_Cliente = "25892     ",
                Contacto = "0  ",
                Ruta = "FW",
                Vendedor = "MPP"
            });
            A.CallTo(() => servicio.LeerPlazosPago(A<string>._, A<string>._)).Returns(new PlazoPago { DtoProntoPago = 0 });
            A.CallTo(() => servicio.LeerDescuentoCliente(A<string>._, A<string>._, A<string>._)).Returns((DescuentosCliente)null);
            A.CallTo(() => servicio.LeerProducto(A<string>._, A<string>._)).ReturnsLazily((string e, string p) => new Producto
            {
                Empresa = "1  ",
                Número = p,
                Nombre = "Producto " + p,
                Estado = 0,
                PVP = 28.01m,
                PrecioMedio = 10m,
                IVA_Repercutido = "G21",
                Grupo = "COS",
                SubGrupo = "COS"
            });
            A.CallTo(() => servicio.LeerPlanCuenta(A<string>._, A<string>._)).Returns(new PlanCuenta { IVA = "G21" });
            // ParámetrosIVA de producción: G21/G21 al 21 % en la empresa 1 y al 0 % en la espejo (las 3 filas de la 3 están a 0).
            A.CallTo(() => servicio.LeerParametroIVA(A<string>.That.Matches(x => x.Trim() == "1"), "G21", A<string>._))
                .Returns(new ParametroIVA { C__IVA = 21, C__RE = 0 });
            A.CallTo(() => servicio.LeerParametroIVA(A<string>.That.Matches(x => x.Trim() == "3"), "G21", A<string>._))
                .Returns(new ParametroIVA { C__IVA = 0, C__RE = 0 });
            A.CallTo(() => servicio.CargarEfectosPedido(A<string>._, A<int>._)).Returns(new List<EfectoPedidoVenta>());
            A.CallTo(() => servicio.ReembolsosCobradosDelPedido(A<string>._, A<int>._, A<DateTime>._)).Returns(0m);
            return servicio;
        }

        private static LineaPedidoVentaDTO LineaProducto(string producto, decimal precio, short cantidad = 1) => new LineaPedidoVentaDTO
        {
            tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            Producto = producto,
            Cantidad = cantidad,
            PrecioUnitario = precio,
            iva = "G21",
            fechaEntrega = DateTime.Today,
            almacen = "ALG",
            formaVenta = "VAR",
            delegacion = "ALG",
            usuario = "NUEVAVISION\\MariaJose"
        };

        private static LineaPedidoVentaDTO LineaPortes() => new LineaPedidoVentaDTO
        {
            tipoLinea = Constantes.TiposLineaVenta.CUENTA_CONTABLE,
            Producto = "62400000",
            Cantidad = 1,
            PrecioUnitario = 3m,
            iva = "G21",
            texto = "Portes",
            fechaEntrega = DateTime.Today,
            almacen = "ALG",
            formaVenta = "VAR",
            delegacion = "ALG",
            usuario = "NUEVAVISION\\MariaJose"
        };

        // ------------------------------------------------------------ líneas de los pedidos

        /// <summary>Una línea con picking ya calculada (Total = Base al 0 %, Base · 1,21 al 21 %).</summary>
        private static LinPedidoVta ConPicking(int pedido, string producto, decimal baseImponible, bool al21)
            => new LinPedidoVta
            {
                Empresa = "1  ",
                Número = pedido,
                Producto = producto,
                Estado = Constantes.EstadosLineaVenta.EN_CURSO,
                Picking = 99941,
                IVA = "G21",
                Base_Imponible = baseImponible,
                PorcentajeIVA = al21 ? (byte)21 : (byte)0,
                ImporteIVA = al21 ? baseImponible * 21 / 100 : 0,
                Total = al21 ? baseImponible + baseImponible * 21 / 100 : baseImponible
            };

        /// <summary>Las 8 líneas originales del 928370 tal como las dejó el PUT del 14/10 (a 0 %).</summary>
        private static List<LinPedidoVta> Originales928370() => new List<LinPedidoVta>
        {
            ConPicking(928370, "35894", 32.95m, false), ConPicking(928370, "45958", 46.60m, false),
            ConPicking(928370, "41260", 14.87m, false), ConPicking(928370, "38162", 22.05m, false),
            ConPicking(928370, "41256", 28.45m, false), ConPicking(928370, "37602", 0m, false),
            ConPicking(928370, "42652", 0m, false), ConPicking(928370, "40586", 0m, false)
        };

        private static List<LinPedidoVta> Lineas928370ComoSalieron()
        {
            List<LinPedidoVta> lineas = Originales928370();
            lineas.Add(ConPicking(928370, "41259", 28.01m, true));
            lineas.Add(ConPicking(928370, "62400000", 3.00m, true));
            return lineas;
        }

        private static List<LinPedidoVta> Lineas929262ComoSalieron() => new List<LinPedidoVta>
        {
            ConPicking(929262, "45958", 466.00m, false), ConPicking(929262, "45958", 0m, false),
            ConPicking(929262, "37559", 31.05m, false),
            ConPicking(929262, "44357", 0m, true), ConPicking(929262, "CHEQUE50_OCT26", -50.00m, true),
            ConPicking(929262, "62400000", 3.00m, true)
        };

        private static CabPedidoVta CabeceraSinIva(int pedido) => new CabPedidoVta
        {
            Empresa = "1  ",
            Número = pedido,
            IVA = null,
            Forma_Pago = "EFC",
            PlazosPago = "CONTADO   ",
            Periodo_Facturacion = "NRM",
            CCC = null,
            MantenerJunto = false
        };

        // ------------------------------------------------------------ 2. el IVA de la línea nueva en el PUT

        [TestMethod]
        public void CrearLineaVtaConIvaCabecera_928370_PedidoPasaASinIva_LasLineasNuevasSalenAl0()
        {
            // En la BD la cabecera aún es G21 (el PUT la pone a null y graba al final): manda la del PUT.
            var gestor = new GestorPedidosVenta(Servicio(928370, ivaCabeceraEnBd: "G21"));

            LinPedidoVta producto = gestor.CrearLineaVtaConIvaCabecera(LineaProducto("41259", 28.01m), EMPRESA, 928370, null, ivaCabecera: null);
            LinPedidoVta portes = gestor.CrearLineaVtaConIvaCabecera(LineaPortes(), EMPRESA, 928370, "MPP", ivaCabecera: null);

            Assert.AreEqual((byte)0, producto.PorcentajeIVA);
            Assert.AreEqual(0m, producto.ImporteIVA);
            Assert.AreEqual(28.01m, producto.Base_Imponible);
            Assert.AreEqual(28.01m, producto.Total, "como las 8 que ya tenía el pedido");
            Assert.AreEqual(3.00m, portes.Total);
            Assert.AreEqual((byte)0, portes.PorcentajeIVA);
            Assert.AreEqual("1  ", producto.Empresa, "la empresa del pedido, la de sus líneas");
        }

        [TestMethod]
        public void CrearLineaVtaConIvaCabecera_928370_ElReembolsoEsElTotalDeLaFactura()
        {
            var gestor = new GestorPedidosVenta(Servicio(928370, ivaCabeceraEnBd: "G21"));
            List<LinPedidoVta> lineas = Originales928370();
            foreach (LinPedidoVta nueva in new[]
            {
                gestor.CrearLineaVtaConIvaCabecera(LineaProducto("41259", 28.01m), EMPRESA, 928370, null, ivaCabecera: null),
                gestor.CrearLineaVtaConIvaCabecera(LineaPortes(), EMPRESA, 928370, "MPP", ivaCabecera: null)
            })
            {
                nueva.Estado = Constantes.EstadosLineaVenta.EN_CURSO;
                nueva.Picking = 99941;
                lineas.Add(nueva);
            }

            ReembolsoCalculado reembolso = GestorEnviosAgencia.CalcularReembolso(CabeceraSinIva(928370),
                Enumerable.Empty<LinPedidoVta>(), lineas, Servicio(928370));

            Assert.AreEqual(175.93m, reembolso.Importe, "GB2601696");
            Assert.AreEqual(175.93m, reembolso.ImporteSegunLineas, "ya no hace falta corregir nada");
            Assert.IsFalse(reembolso.Corregido);
            Assert.IsFalse(reembolso.IvaMezclado);
        }

        [TestMethod]
        public void CrearLineaVta_LeeLaCabeceraDeLaBd_EsLoQueProvocabaEl21()
        {
            // El camino de siempre (POST, Nesto…) sigue leyendo la cabecera de la BD: documenta la causa del 928370.
            var gestor = new GestorPedidosVenta(Servicio(928370, ivaCabeceraEnBd: "G21"));

            LinPedidoVta linea = gestor.CrearLineaVta(LineaProducto("41259", 28.01m), EMPRESA, 928370);

            Assert.AreEqual((byte)21, linea.PorcentajeIVA);
            Assert.AreEqual(33.8921m, linea.Total);
        }

        [TestMethod]
        public void CrearLineaVtaConIvaCabecera_PedidoNormalEmpresa1G21_NoCambia()
        {
            var gestor = new GestorPedidosVenta(Servicio(930000, ivaCabeceraEnBd: "G21"));

            LinPedidoVta linea = gestor.CrearLineaVtaConIvaCabecera(LineaProducto("41259", 28.01m), EMPRESA, 930000, null, ivaCabecera: "G21");

            Assert.AreEqual((byte)21, linea.PorcentajeIVA);
            Assert.AreEqual(28.01m, linea.Base_Imponible);
            Assert.AreEqual(5.8821m, linea.ImporteIVA);
            Assert.AreEqual(33.8921m, linea.Total);
            Assert.AreEqual("G21", linea.IVA);
        }

        // ------------------------------------------------------------ 3. red de seguridad en la etiqueta

        [TestMethod]
        public void CalcularReembolso_928370_LineasMezcladas_CobraLoQueCobraLaFactura()
        {
            ReembolsoCalculado reembolso = GestorEnviosAgencia.CalcularReembolso(CabeceraSinIva(928370),
                Enumerable.Empty<LinPedidoVta>(), Lineas928370ComoSalieron(), Servicio(928370));

            Assert.AreEqual(182.44m, reembolso.ImporteSegunLineas, "lo que salió en la etiqueta y en CTT");
            Assert.AreEqual(175.93m, reembolso.Importe, "GB2601696");
            Assert.IsTrue(reembolso.FacturaPorEmpresaEspejo);
            Assert.IsTrue(reembolso.IvaMezclado);
            Assert.IsTrue(reembolso.Corregido);
            StringAssert.Contains(reembolso.Explicacion, "NestoAPI#718");
            StringAssert.Contains(reembolso.Explicacion, "mezclados");
        }

        [TestMethod]
        public void ImporteReembolso_929262_ConChequeNegativo_CobraLoQueCobraLaFactura()
        {
            decimal importe = GestorEnviosAgencia.ImporteReembolso(CabeceraSinIva(929262),
                Enumerable.Empty<LinPedidoVta>(), Lineas929262ComoSalieron(), Servicio(929262));
            ReembolsoCalculado reembolso = GestorEnviosAgencia.CalcularReembolso(CabeceraSinIva(929262),
                Enumerable.Empty<LinPedidoVta>(), Lineas929262ComoSalieron(), Servicio(929262));

            Assert.AreEqual(450.05m, importe, "GB2601695");
            Assert.AreEqual(440.18m, reembolso.ImporteSegunLineas, "lo que salió en la etiqueta");
            Assert.IsTrue(reembolso.IvaMezclado);
        }

        [TestMethod]
        public void CalcularReembolso_PedidoYaEnLaEmpresaEspejo_TambienSinIva()
        {
            CabPedidoVta cabecera = CabeceraSinIva(928370);
            cabecera.Empresa = "3  ";
            cabecera.IVA = "G21"; // el traspaso copia la cabecera con IVA_POR_DEFECTO

            decimal importe = GestorEnviosAgencia.ImporteReembolso(cabecera,
                Enumerable.Empty<LinPedidoVta>(), Lineas928370ComoSalieron(), Servicio(928370));

            Assert.AreEqual(175.93m, importe);
        }

        [TestMethod]
        public void CalcularReembolso_PedidoNormalEmpresa1G21_NoCambia()
        {
            CabPedidoVta cabecera = CabeceraSinIva(930000);
            cabecera.IVA = "G21";
            IServicioPedidosVenta servicio = Servicio(930000);
            var lineas = new List<LinPedidoVta>
            {
                ConPicking(930000, "41259", 28.01m, true), ConPicking(930000, "62400000", 3.00m, true)
            };

            ReembolsoCalculado reembolso = GestorEnviosAgencia.CalcularReembolso(cabecera, Enumerable.Empty<LinPedidoVta>(), lineas, servicio);

            Assert.AreEqual(37.52m, reembolso.Importe, "33,8921 + 3,63");
            Assert.IsFalse(reembolso.FacturaPorEmpresaEspejo);
            Assert.IsFalse(reembolso.Corregido);
            Assert.IsNull(reembolso.Explicacion);
            A.CallTo(() => servicio.LeerParametroIVA(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void HayIvaMezclado_TodasAl0OTodasAl21_No()
        {
            Assert.IsFalse(GestorEnviosAgencia.HayIvaMezclado(Originales928370()));
            Assert.IsFalse(GestorEnviosAgencia.HayIvaMezclado(new[] { ConPicking(1, "A", 10m, true), ConPicking(1, "B", 0m, false) }),
                "las líneas a 0 € no cuentan");
            Assert.IsTrue(GestorEnviosAgencia.HayIvaMezclado(Lineas928370ComoSalieron()));
        }

        [TestMethod]
        public void FilaCorreccionReembolso_ConCorreccion_DejaLaHistoriaDelEnvio()
        {
            ReembolsoCalculado reembolso = GestorEnviosAgencia.CalcularReembolso(CabeceraSinIva(928370),
                Enumerable.Empty<LinPedidoVta>(), Lineas928370ComoSalieron(), Servicio(928370));
            var propuesta = new PropuestaEnvioDTO { CorreccionReembolso = reembolso.Explicacion, ReembolsoSegunLineas = reembolso.ImporteSegunLineas };
            var envio = new EnviosAgencia { Numero = 250264, Reembolso = reembolso.Importe };
            var ahora = new DateTime(2026, 10, 27, 12, 0, 0);

            EnvioHistoria fila = ServicioEnvioDesdePedido.FilaCorreccionReembolso(envio, propuesta, "NUEVAVISION\\Andre", ahora);

            Assert.IsNotNull(fila);
            Assert.AreEqual(250264, fila.NumeroEnvio);
            Assert.AreEqual("Reembolso", fila.Campo);
            Assert.AreEqual("182.44", fila.ValorAnterior);
            Assert.AreEqual(reembolso.Explicacion, fila.Observaciones);
            Assert.AreEqual("NUEVAVISION\\Andre", fila.Usuario);
            Assert.AreEqual(ahora, fila.FechaModificacion);
        }

        [TestMethod]
        public void FilaCorreccionReembolso_SinCorreccionOSinReembolso_NoHayFila()
        {
            var envio = new EnviosAgencia { Numero = 1, Reembolso = 10m };
            Assert.IsNull(ServicioEnvioDesdePedido.FilaCorreccionReembolso(envio, new PropuestaEnvioDTO(), "x", DateTime.Now));
            var propuesta = new PropuestaEnvioDTO { CorreccionReembolso = "NestoAPI#718: …", ReembolsoSegunLineas = 12m };
            Assert.IsNull(ServicioEnvioDesdePedido.FilaCorreccionReembolso(new EnviosAgencia { Numero = 1, Reembolso = 0m }, propuesta, "x", DateTime.Now));
            Assert.IsNotNull(ServicioEnvioDesdePedido.FilaCorreccionReembolso(envio, propuesta, "x", DateTime.Now));
            propuesta.Origen = PropuestaEnvioDTO.ORIGEN_PENDIENTE_REUTILIZADO;
            Assert.IsNull(ServicioEnvioDesdePedido.FilaCorreccionReembolso(envio, propuesta, "x", DateTime.Now), "el pendiente reutilizado conserva su reembolso");
        }
    }
}
