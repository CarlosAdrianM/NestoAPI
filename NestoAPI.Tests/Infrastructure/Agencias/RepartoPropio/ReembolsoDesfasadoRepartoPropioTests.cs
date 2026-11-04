using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.RepartoPropio;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.Facturas.Agrupacion;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Pedidos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#810 (pedido 928723, envío RP250518 con 480,02 € y factura GB2601741 de 510,15 €; Alfredo, sugerencia 1005): al facturar
    /// un pedido con envío del reparto propio sin entregar y reembolso desfasado, la respuesta lo dice (ReembolsosDesfasados) sin
    /// cambiar nada; PUT api/EnviosAgencias/{id}/Reembolso lo cambia (solo el campo y la historia, sin contabilidad).
    /// </summary>
    [TestClass]
    public class ReembolsoDesfasadoRepartoPropioTests
    {
        private const int ENVIO = 250518;
        private const int PEDIDO = 928723;
        private const string CLIENTE = "2077";
        private const string FACTURA = "GB2601741";

        private static EnviosAgencia Envio(decimal reembolso = 480.02M, short estado = Constantes.Agencias.ESTADO_TRAMITADO,
            int agencia = Constantes.Agencias.AGENCIA_REPARTO_PROPIO, int numero = ENVIO) => new EnviosAgencia
        {
            Numero = numero,
            Empresa = "1  ",
            Agencia = agencia,
            Cliente = CLIENTE + "     ",
            Pedido = PEDIDO,
            Estado = estado,
            Reembolso = reembolso,
            Fecha = new DateTime(2026, 11, 3),
            FechaPagoReembolso = new DateTime(2026, 11, 3)
        };

        private static List<FacturaPedidoReparto> Facturas() => new List<FacturaPedidoReparto>
        {
            new FacturaPedidoReparto { Pedido = PEDIDO, Empresa = "3  ", Factura = FACTURA + "   " }
        };

        private static ExtractoCliente Efecto(decimal pendiente, string documento = FACTURA, string formaPago = Constantes.FormasPago.EFECTIVO,
            string cliente = CLIENTE, string tipo = Constantes.TiposApunteExtracto.EFECTO) => new ExtractoCliente
        {
            Empresa = "3  ",
            Número = cliente + "     ",
            Nº_Documento = documento + "   ",
            TipoApunte = tipo,
            FormaPago = formaPago + " ",
            ImportePdte = pendiente,
            Fecha = new DateTime(2026, 11, 3)
        };

        // ---------------------------------------------------------------- la regla

        [TestMethod]
        public void Detectar_Caso928723_EnvioConLoDeAntesDeLaAmpliacion_SaleDesfasado()
        {
            List<ReembolsoDesfasadoDTO> desfasados = ReembolsoDesfasadoRepartoPropio.Detectar(
                new[] { Envio() }, Facturas(), new[] { Efecto(510.15M) });

            ReembolsoDesfasadoDTO desfasado = desfasados.Single();
            Assert.AreEqual(ENVIO, desfasado.Envio);
            Assert.AreEqual(PEDIDO, desfasado.Pedido);
            Assert.AreEqual(480.02M, desfasado.ReembolsoActual);
            Assert.AreEqual(510.15M, desfasado.ReembolsoFacturado);
        }

        [TestMethod]
        public void Detectar_ReembolsoQueCuadraConLaFactura_Nada()
        {
            Assert.AreEqual(0, ReembolsoDesfasadoRepartoPropio.Detectar(new[] { Envio(510.15M) }, Facturas(), new[] { Efecto(510.15M) }).Count);
        }

        [DataTestMethod]
        [DataRow(Constantes.Agencias.ESTADO_ENTREGADO)]
        [DataRow(Constantes.Agencias.ESTADO_DEVUELTO)]
        [DataRow(Constantes.Agencias.ESTADO_CERRADO_A_MANO)]
        public void Detectar_EnvioYaEntregadoODevuelto_Nada(short estado)
        {
            Assert.AreEqual(0, ReembolsoDesfasadoRepartoPropio.Detectar(new[] { Envio(estado: estado) }, Facturas(), new[] { Efecto(510.15M) }).Count);
        }

        [TestMethod]
        public void Detectar_EnvioDeOtraAgencia_Nada()
        {
            Assert.AreEqual(0, ReembolsoDesfasadoRepartoPropio.Detectar(
                new[] { Envio(agencia: Constantes.Agencias.AGENCIA_CTT) }, Facturas(), new[] { Efecto(510.15M) }).Count);
        }

        [TestMethod]
        public void Detectar_PedidoSinFactura_Nada()
        {
            Assert.AreEqual(0, ReembolsoDesfasadoRepartoPropio.Detectar(new[] { Envio() }, new List<FacturaPedidoReparto>(), new[] { Efecto(510.15M) }).Count);
        }

        [TestMethod]
        public void Detectar_SinEnvios_Nada()
        {
            Assert.AreEqual(0, ReembolsoDesfasadoRepartoPropio.Detectar(null, Facturas(), new[] { Efecto(510.15M) }).Count);
        }

        [TestMethod]
        public void Detectar_SoloCuentaLoPendienteEnEfectivoDeLasFacturasDelCliente()
        {
            var extracto = new[]
            {
                Efecto(500M),
                Efecto(10.15M),
                Efecto(99M, formaPago: "TRN"),          // no se cobra a la entrega
                Efecto(99M, cliente: "15191"),           // otro cliente
                Efecto(99M, documento: "GB2600001"),     // otra factura
                Efecto(99M, tipo: Constantes.TiposApunteExtracto.FACTURA)
            };

            Assert.AreEqual(510.15M, ReembolsoDesfasadoRepartoPropio.Detectar(new[] { Envio() }, Facturas(), extracto).Single().ReembolsoFacturado);
        }

        [TestMethod]
        public void Detectar_FacturaYaCobrada_ElEnvioConReembolsoSaleACero()
        {
            ReembolsoDesfasadoDTO desfasado = ReembolsoDesfasadoRepartoPropio.Detectar(new[] { Envio() }, Facturas(), new ExtractoCliente[0]).Single();

            Assert.AreEqual(0M, desfasado.ReembolsoFacturado);
        }

        [TestMethod]
        public void Detectar_DosEnviosSinEntregarDelMismoPedido_ACadaUnoLoQueNoLlevaElOtro()
        {
            List<ReembolsoDesfasadoDTO> desfasados = ReembolsoDesfasadoRepartoPropio.Detectar(
                new[] { Envio(480.02M), Envio(20M, numero: ENVIO + 1) }, Facturas(), new[] { Efecto(510.15M) });

            Assert.AreEqual(490.15M, desfasados.Single(d => d.Envio == ENVIO).ReembolsoFacturado);
            Assert.AreEqual(30.13M, desfasados.Single(d => d.Envio == ENVIO + 1).ReembolsoFacturado);
        }

        // ---------------------------------------------------------------- FacturarPedido

        private static FacturacionRutasController Facturacion(IGestorFacturacionRutas gestor, IDetectorReembolsosDesfasados detector,
            CabPedidoVta pedido)
        {
            var servicioPedidos = A.Fake<IServicioPedidosParaFacturacion>();
            A.CallTo(() => servicioPedidos.ObtenerPedidoParaFacturar(A<string>._, A<int>._, A<DateTime>._)).Returns(pedido);
            var guardia = A.Fake<IGuardiaNotaSinPacking>();
            A.CallTo(() => guardia.Motivo(A<string>._, A<int>._)).Returns(Task.FromResult<string>(null));
            var controller = new FacturacionRutasController(A.Fake<NVEntities>(), servicioPedidos, A.Fake<IServicioAgruparPorPO>(), () => gestor)
            {
                GuardiaNotas = guardia,
                DetectorReembolsos = detector
            };
            var identity = new ClaimsIdentity("TestAuth");
            identity.AddClaim(new Claim(ClaimTypes.Role, Constantes.GruposSeguridad.ALMACEN));
            identity.AddClaim(new Claim(ClaimTypes.Name, @"NUEVAVISION\Alfredo"));
            controller.User = new ClaimsPrincipal(identity);
            return controller;
        }

        private static FacturarRutasResponseDTO ConFactura()
        {
            var respuesta = new FacturarRutasResponseDTO();
            respuesta.Facturas.Add(new FacturaCreadaDTO { Empresa = "3", NumeroPedido = PEDIDO, NumeroFactura = FACTURA, Cliente = CLIENTE });
            return respuesta;
        }

        [TestMethod]
        public async Task FacturarPedido_ConEnvioDeRepartoPropioDesfasado_LoDevuelveSinCambiarNada()
        {
            var gestor = A.Fake<IGestorFacturacionRutas>();
            A.CallTo(() => gestor.FacturarRutas(A<List<CabPedidoVta>>._, A<string>._, A<DateTime>._)).Returns(ConFactura());
            var detector = A.Fake<IDetectorReembolsosDesfasados>();
            A.CallTo(() => detector.Detectar(A<IEnumerable<int>>.That.Matches(p => p.Contains(PEDIDO)))).Returns(new List<ReembolsoDesfasadoDTO>
            {
                new ReembolsoDesfasadoDTO { Envio = ENVIO, Pedido = PEDIDO, ReembolsoActual = 480.02M, ReembolsoFacturado = 510.15M }
            });

            var resultado = await Facturacion(gestor, detector, new CabPedidoVta { Empresa = "1", Número = PEDIDO })
                .FacturarPedido(new FacturarPedidoRequestDTO { Empresa = "1", Pedido = PEDIDO }) as OkNegotiatedContentResult<FacturarRutasResponseDTO>;

            ReembolsoDesfasadoDTO desfasado = resultado.Content.ReembolsosDesfasados.Single();
            Assert.AreEqual(ENVIO, desfasado.Envio);
            Assert.AreEqual(510.15M, desfasado.ReembolsoFacturado);
        }

        [TestMethod]
        public async Task FacturarPedido_SinEnvioDesfasado_ListaVacia()
        {
            var gestor = A.Fake<IGestorFacturacionRutas>();
            A.CallTo(() => gestor.FacturarRutas(A<List<CabPedidoVta>>._, A<string>._, A<DateTime>._)).Returns(ConFactura());
            var detector = A.Fake<IDetectorReembolsosDesfasados>();
            A.CallTo(() => detector.Detectar(A<IEnumerable<int>>._)).Returns(new List<ReembolsoDesfasadoDTO>());

            var resultado = await Facturacion(gestor, detector, new CabPedidoVta { Empresa = "1", Número = PEDIDO })
                .FacturarPedido(new FacturarPedidoRequestDTO { Empresa = "1", Pedido = PEDIDO }) as OkNegotiatedContentResult<FacturarRutasResponseDTO>;

            Assert.IsNotNull(resultado.Content.ReembolsosDesfasados);
            Assert.AreEqual(0, resultado.Content.ReembolsosDesfasados.Count);
        }

        [TestMethod]
        public async Task FacturarPedido_SinFacturaCreada_NoMiraLosEnvios()
        {
            var gestor = A.Fake<IGestorFacturacionRutas>();
            A.CallTo(() => gestor.FacturarRutas(A<List<CabPedidoVta>>._, A<string>._, A<DateTime>._)).Returns(new FacturarRutasResponseDTO());
            var detector = A.Fake<IDetectorReembolsosDesfasados>();

            var resultado = await Facturacion(gestor, detector, new CabPedidoVta { Empresa = "1", Número = PEDIDO })
                .FacturarPedido(new FacturarPedidoRequestDTO { Empresa = "1", Pedido = PEDIDO }) as OkNegotiatedContentResult<FacturarRutasResponseDTO>;

            Assert.AreEqual(0, resultado.Content.ReembolsosDesfasados.Count);
            A.CallTo(() => detector.Detectar(A<IEnumerable<int>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task FacturarPedido_SiFallaLaComprobacion_FacturaIgualYListaVacia()
        {
            var gestor = A.Fake<IGestorFacturacionRutas>();
            A.CallTo(() => gestor.FacturarRutas(A<List<CabPedidoVta>>._, A<string>._, A<DateTime>._)).Returns(ConFactura());
            var detector = A.Fake<IDetectorReembolsosDesfasados>();
            A.CallTo(() => detector.Detectar(A<IEnumerable<int>>._)).Throws(new InvalidOperationException("BD caída"));

            var resultado = await Facturacion(gestor, detector, new CabPedidoVta { Empresa = "1", Número = PEDIDO })
                .FacturarPedido(new FacturarPedidoRequestDTO { Empresa = "1", Pedido = PEDIDO }) as OkNegotiatedContentResult<FacturarRutasResponseDTO>;

            Assert.AreEqual(1, resultado.Content.Facturas.Count);
            Assert.AreEqual(0, resultado.Content.ReembolsosDesfasados.Count);
        }

        // ---------------------------------------------------------------- PUT api/EnviosAgencias/{id}/Reembolso

        private NVEntities db;
        private List<EnvioHistoria> historiaGuardada;
        private ITramitacionEnviosService tramitacion;
        private DbSet<EnviosAgencia> fakeEnvios;

        private EnviosAgenciasController Envios(EnviosAgencia envio)
        {
            db = A.Fake<NVEntities>();
            fakeEnvios = A.Fake<DbSet<EnviosAgencia>>(o => o.Implements<IQueryable<EnviosAgencia>>().Implements<IDbAsyncEnumerable<EnviosAgencia>>());
            var fakeHistoria = A.Fake<DbSet<EnvioHistoria>>(o => o.Implements<IQueryable<EnvioHistoria>>().Implements<IDbAsyncEnumerable<EnvioHistoria>>());
            historiaGuardada = new List<EnvioHistoria>();
            A.CallTo(() => fakeHistoria.Add(A<EnvioHistoria>._)).ReturnsLazily((EnvioHistoria h) => { historiaGuardada.Add(h); return h; });
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.EnviosHistorias).Returns(fakeHistoria);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            A.CallTo(() => fakeEnvios.FindAsync(envio.Numero)).Returns(Task.FromResult(envio));
            tramitacion = A.Fake<ITramitacionEnviosService>();
            return new EnviosAgenciasController(db, tramitacion)
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, @"NUEVAVISION\Alfredo") }, "Bearer"))
            };
        }

        [TestMethod]
        public async Task PutReembolso_RepartoPropioSinEntregar_CambiaElCampoConHistoriaYSinContabilidad()
        {
            EnviosAgencia envio = Envio();

            var resultado = await Envios(envio).CambiarReembolsoRepartoPropio(ENVIO,
                new CambiarReembolsoEnvioDTO { Reembolso = 510.15M, Observaciones = "Factura GB2601741 tras la ampliación" })
                as OkNegotiatedContentResult<ReembolsoEnvioCambiadoDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(510.15M, envio.Reembolso);
            Assert.AreEqual(480.02M, resultado.Content.ReembolsoAnterior);
            Assert.AreEqual(510.15M, resultado.Content.Reembolso);
            Assert.AreEqual(PEDIDO, resultado.Content.Pedido);
            Assert.IsTrue(resultado.Content.Cambiado);
            Assert.AreEqual(Constantes.Agencias.ESTADO_TRAMITADO, envio.Estado, "No cambia el estado");
            EnvioHistoria fila = historiaGuardada.Single();
            Assert.AreEqual(ENVIO, fila.NumeroEnvio);
            Assert.AreEqual("Reembolso", fila.Campo);
            Assert.AreEqual("480.02", fila.ValorAnterior);
            StringAssert.Contains(fila.Observaciones, "Factura GB2601741 tras la ampliación");
            StringAssert.Contains(fila.Observaciones, "480,02 € → 510,15 €");
            Assert.IsFalse(string.IsNullOrWhiteSpace(fila.Usuario));
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
            Assert.AreEqual(0, Fake.GetCalls(tramitacion).Count(), "Sin apuntes: no se llama a la tramitación (contabilidad)");
        }

        [TestMethod]
        public async Task PutReembolso_MismoImporte_NoEscribeNada()
        {
            var resultado = await Envios(Envio()).CambiarReembolsoRepartoPropio(ENVIO, new CambiarReembolsoEnvioDTO { Reembolso = 480.02M })
                as OkNegotiatedContentResult<ReembolsoEnvioCambiadoDTO>;

            Assert.IsFalse(resultado.Content.Cambiado);
            Assert.AreEqual(0, historiaGuardada.Count);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutReembolso_RepartoPropioEntregado_409()
        {
            EnviosAgencia envio = Envio(estado: Constantes.Agencias.ESTADO_ENTREGADO);

            var resultado = await Envios(envio).CambiarReembolsoRepartoPropio(ENVIO, new CambiarReembolsoEnvioDTO { Reembolso = 510.15M })
                as NegotiatedContentResult<string>;

            Assert.AreEqual(HttpStatusCode.Conflict, resultado?.StatusCode);
            Assert.AreEqual(480.02M, envio.Reembolso);
            Assert.AreEqual(0, historiaGuardada.Count);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutReembolso_EnvioDeCtt_400()
        {
            EnviosAgencia envio = Envio(agencia: Constantes.Agencias.AGENCIA_CTT);

            var resultado = await Envios(envio).CambiarReembolsoRepartoPropio(ENVIO, new CambiarReembolsoEnvioDTO { Reembolso = 510.15M });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            StringAssert.Contains(((BadRequestErrorMessageResult)resultado).Message, "no es del reparto propio");
            Assert.AreEqual(480.02M, envio.Reembolso);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutReembolso_Negativo_400()
        {
            var resultado = await Envios(Envio()).CambiarReembolsoRepartoPropio(ENVIO, new CambiarReembolsoEnvioDTO { Reembolso = -1M });

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task PutReembolso_EnvioQueNoExiste_404()
        {
            EnviosAgenciasController controller = Envios(Envio());
            A.CallTo(() => fakeEnvios.FindAsync(999)).Returns(Task.FromResult<EnviosAgencia>(null));

            var resultado = await controller.CambiarReembolsoRepartoPropio(999, new CambiarReembolsoEnvioDTO { Reembolso = 1M });

            Assert.IsInstanceOfType(resultado, typeof(NotFoundResult));
        }

        [TestMethod]
        public void CambioReembolso_AReparto_PonLaFechaDePagoSiNoLaTenia()
        {
            // NestoAPI#778: el reembolso del reparto propio nace «pagado» (lo cobra nuestro repartidor)
            EnviosAgencia envio = Envio(0M);
            envio.FechaPagoReembolso = null;

            _ = CambioReembolsoRepartoPropio.Aplicar(envio, 30.13M, null, "Alfredo", new DateTime(2026, 11, 4, 9, 0, 0));

            Assert.AreEqual(new DateTime(2026, 11, 3), envio.FechaPagoReembolso);
        }
    }
}
