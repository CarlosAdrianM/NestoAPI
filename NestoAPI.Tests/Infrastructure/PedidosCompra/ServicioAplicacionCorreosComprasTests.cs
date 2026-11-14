using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento;
using NestoAPI.Infraestructure.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PedidosCompra
{
    /// <summary>
    /// NestoAPI#857 (fase 3): de punta a punta sin BD (repositorio en memoria). El caso real del 12/11/26: un envío de 3 bultos de Weelko
    /// por UPS contratado en MBE: tres avisos de UPS (la fecha, sin proveedor) y uno de MBE (el proveedor y los tres códigos).
    /// </summary>
    [TestClass]
    public class ServicioAplicacionCorreosComprasTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 11, 12, 21, 30, 0);
        private static readonly DateTime DIA_UPS = new DateTime(2026, 11, 16);

        private RepositorioCorreosSeguimientoEnMemoria repositorio;
        private ILectorBuzonCompras lector;
        private LecturaBuzonCompras lectura;
        private List<Tuple<CorreoSeguimientoFila, int>> avisos;
        private List<string> promesas;
        private List<Exception> errores;
        private bool interruptor;
        private DateTime ahora;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = new RepositorioCorreosSeguimientoEnMemoria();
            avisos = new List<Tuple<CorreoSeguimientoFila, int>>();
            promesas = new List<string>();
            errores = new List<Exception>();
            interruptor = true;
            ahora = AHORA;
            lectura = new LecturaBuzonCompras();
            lector = A.Fake<ILectorBuzonCompras>();
            A.CallTo(() => lector.Buzon).Returns("buzon@ejemplo.test");
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).ReturnsLazily(() => Task.FromResult(lectura));
        }

        private ServicioAplicacionCorreosCompras Aplicacion()
        {
            IAvisadorCorreosPorAsignar avisador = A.Fake<IAvisadorCorreosPorAsignar>();
            A.CallTo(() => avisador.Avisar(A<CorreoSeguimientoFila>._, A<int>._)).ReturnsLazily((CorreoSeguimientoFila f, int n) =>
            {
                avisos.Add(Tuple.Create(f, n));
                return Task.CompletedTask;
            });
            return new ServicioAplicacionCorreosCompras(repositorio, () => interruptor, avisador,
                (empresa, productos, descripcion, causas) => promesas.AddRange(productos), () => ahora, errores.Add);
        }

        private ServicioCorreosSeguimientoCompras Nucleo(bool conAplicacion = true) => new ServicioCorreosSeguimientoCompras(lector, repositorio, errores.Add,
            horaCorte: () => new TimeSpan(11, 0, 0), esFestivo: _ => false, aplicacion: conAplicacion ? Aplicacion() : null);

        private async Task Pasada(params CorreoCompras[] correos)
        {
            lectura = new LecturaBuzonCompras { Correos = correos.ToList() };
            _ = await Nucleo().Pasada(ahora);
        }

        /// <summary>
        /// Un correo por pasada, llegando seguidos en ese orden (20 s entre uno y otro; cada pasada, un minuto después de su correo) y,
        /// al final, una pasada vacía a las 21:30 (cuando ya se puede avisar).
        /// </summary>
        private async Task UnoPorPasada(CorreoCompras[] correos)
        {
            DateTime llegada = new DateTime(2026, 11, 12, 20, 15, 39);
            foreach (CorreoCompras correo in correos)
            {
                correo.Recibido = llegada;
                ahora = llegada.AddMinutes(1);
                await Pasada(correo);
                llegada = llegada.AddSeconds(20);
            }
            ahora = AHORA;
            await Pasada();
        }

        private static CorreoCompras Ups(string id, string codigo, int h, int m, int s) => CorreosSeguimientoFase3Tests.Correo(
            string.Format(CorreosSeguimientoFase3Tests.UPS_TEXTO, codigo), "pkginfo@ups.com", new DateTime(2026, 11, 12, h, m, s), "Tu paquete está de camino", id);

        private static CorreoCompras Mbe() => CorreosSeguimientoFase3Tests.Correo(CorreosSeguimientoFase3Tests.MBE_TEXTO, "recogidas061@mbe.es",
            new DateTime(2026, 11, 12, 20, 15, 47), "Sus envíos con MBE 12/11/2026", "mbe");

        private static Dictionary<string, CorreoCompras> Envio() => new Dictionary<string, CorreoCompras>
        {
            ["UPS1"] = Ups("ups1", "1Z21E7X76890915153", 20, 15, 39),
            ["MBE"] = Mbe(),
            ["UPS2"] = Ups("ups2", "1Z21E7X76890089163", 20, 16, 1),
            ["UPS3"] = Ups("ups3", "1Z21E7X76893119777", 21, 2, 0)
        };

        private void UnPedidoDeWeelko() =>
            repositorio.AnadirPedido(912001, "9100", "WEELKO BARCELONA SLU", new DateTime(2026, 11, 11, 7, 39, 0), new DateTime(2026, 11, 20), null);

        private void DosPedidosDeWeelko()
        {
            repositorio.AnadirPedido(911500, "9100", "WEELKO BARCELONA SLU", new DateTime(2026, 9, 7, 10, 0, 0), new DateTime(2026, 9, 20));
            repositorio.Pedidos.Last().LineasYaRecibidas = 3;
            UnPedidoDeWeelko();
        }

        private CorreoCompras[] EnOrden(string orden)
        {
            Dictionary<string, CorreoCompras> envio = Envio();
            return orden.Split(',').Select(o => envio[o]).ToArray();
        }

        #region Un pedido de Weelko en el aire: se aplica solo

        [DataTestMethod]
        [DataRow("UPS1,MBE,UPS2,UPS3")]
        [DataRow("MBE,UPS1,UPS2,UPS3")]
        [DataRow("UPS1,UPS2,UPS3,MBE")]
        public async Task UnPedido_EnUnaPasada_SeAplicaEl16AlPedidoUnaVezSinAviso(string orden)
        {
            UnPedidoDeWeelko();

            await Pasada(EnOrden(orden));

            Assert.AreEqual(4, repositorio.Correos.Count);
            Assert.IsTrue(repositorio.Correos.All(c => c.PedidoAplicado == 912001 && c.FechaRecepcionAplicada == DIA_UPS), "Los cuatro correos, al pedido de Weelko");
            Assert.IsTrue(repositorio.Lineas[912001].All(l => l.FechaRecepcion == DIA_UPS));
            Assert.AreEqual(1, repositorio.Rastros.Count, "Un solo rastro: los demás encuentran ya la fecha");
            Assert.AreEqual(0, avisos.Count);
            Assert.IsTrue(repositorio.Correos.All(c => c.AplicadoPor == ServicioAplicacionCorreosCompras.APLICADO_POR_AUTOMATICO));
            Assert.AreEqual(0, errores.Count, string.Join(" | ", errores.Select(e => e.Message)));
        }

        [DataTestMethod]
        [DataRow("UPS1,MBE,UPS2,UPS3")]
        [DataRow("MBE,UPS1,UPS2,UPS3")]
        [DataRow("UPS1,UPS2,UPS3,MBE")]
        public async Task UnPedido_UnCorreoPorPasada_AcabaEn16_SinRastroRepetidoSiLaFechaNoCambia(string orden)
        {
            UnPedidoDeWeelko();

            await UnoPorPasada(EnOrden(orden));

            Assert.IsTrue(repositorio.Correos.All(c => c.PedidoAplicado == 912001));
            Assert.IsTrue(repositorio.Lineas[912001].All(l => l.FechaRecepcion == DIA_UPS));
            Assert.AreEqual(DIA_UPS, repositorio.Rastros.Last().Fecha);
            Assert.AreEqual(repositorio.Rastros.Count, repositorio.Rastros.Select(r => r.Fecha).Distinct().Count(), "Nunca dos rastros con la misma fecha");
            Assert.IsTrue(avisos.Count <= 1, "Como mucho un aviso por envío");
            if (orden.StartsWith("MBE"))
            {
                // MBE no trae fecha: primero recibido + 24 h de UPS (13/11) y, al llegar la de UPS, el 16/11
                CollectionAssert.AreEqual(new[] { new DateTime(2026, 11, 13), DIA_UPS }, repositorio.Rastros.Select(r => r.Fecha).ToList());
            }
            else
            {
                Assert.AreEqual(1, repositorio.Rastros.Count);
            }
        }

        [TestMethod]
        public async Task UnPedido_InterruptorApagado_NoEscribeNiAvisa_YQuedaPorAsignar()
        {
            UnPedidoDeWeelko();
            interruptor = false;

            await Pasada(EnOrden("UPS1,MBE,UPS2,UPS3"));

            Assert.AreEqual(0, repositorio.LlamadasAplicar);
            Assert.AreEqual(new DateTime(2026, 11, 20), repositorio.Lineas[912001][0].FechaRecepcion);
            Assert.AreEqual(0, avisos.Count);
            PorAsignarCorreosCompraDTO porAsignar = await PorAsignar();
            Assert.AreEqual(1, porAsignar.Envios.Count);
            Assert.AreEqual(912001, porAsignar.Envios[0].Candidatos.Single().Pedido);
        }

        [TestMethod]
        public async Task SinScript_ComoAntes_NiAplicaNiAvisaNiSeAsigna()
        {
            UnPedidoDeWeelko();
            repositorio.ConAplicacion = false;

            await Pasada(EnOrden("UPS1,MBE,UPS2,UPS3"));

            Assert.AreEqual(4, repositorio.Correos.Count, "Se guarda como siempre");
            Assert.AreEqual(0, repositorio.LlamadasAplicar);
            Assert.AreEqual(0, avisos.Count);
            ResultadoOperacionCorreoCompra asignar = await Aplicacion().Asignar(1, new PeticionAsignarCorreoCompraDTO { Pedido = 912001 }, false, "Prueba");
            Assert.AreEqual(CodigoOperacionCorreoCompra.SinTabla, asignar.Codigo);
        }

        [TestMethod]
        public async Task ElHistorico_NuncaEscribeEnCompras()
        {
            UnPedidoDeWeelko();
            var resultado = new ResultadoPasadaCorreosSeguimiento();

            _ = await Nucleo().GuardarCorreos(EnOrden("UPS1,MBE,UPS2,UPS3").ToList(), resultado,
                _ => repositorio.LeerPedidosEnAire("1"));

            Assert.AreEqual(4, resultado.Guardados);
            Assert.AreEqual(0, repositorio.LlamadasAplicar);
            Assert.AreEqual(0, avisos.Count);
        }

        [TestMethod]
        public async Task Idempotente_ElMismoCorreoOtraVezNoVuelveAAplicar()
        {
            UnPedidoDeWeelko();
            await Pasada(EnOrden("UPS1,MBE,UPS2,UPS3"));
            int llamadas = repositorio.LlamadasAplicar;

            await Pasada(EnOrden("UPS1,MBE,UPS2,UPS3"));

            Assert.AreEqual(llamadas, repositorio.LlamadasAplicar);
            Assert.AreEqual(1, repositorio.Rastros.Count);
        }

        [TestMethod]
        public async Task CorreoDeHaceMasDeTresDias_NoSeAplicaSolo()
        {
            UnPedidoDeWeelko();
            CorreoCompras mbe = Mbe();
            mbe.Recibido = AHORA.AddDays(-4);

            await Pasada(mbe);

            Assert.AreEqual(0, repositorio.LlamadasAplicar);
            Assert.AreEqual(0, avisos.Count);
        }

        [TestMethod]
        public async Task SiLaNuevaFechaRetrasaLineas_SeAnotaParaLaPromesaEnRiesgo()
        {
            repositorio.AnadirPedido(912001, "9100", "WEELKO BARCELONA SLU", new DateTime(2026, 11, 11), new DateTime(2026, 11, 14));

            await Pasada(EnOrden("UPS1,MBE"));

            CollectionAssert.AreEqual(new[] { "P0" }, promesas);
        }

        #endregion

        #region Dos pedidos de Weelko: por asignar, un aviso, y Compras decide

        [DataTestMethod]
        [DataRow("UPS1,UPS2,MBE,UPS3")]
        [DataRow("MBE,UPS1,UPS2,UPS3")]
        [DataRow("UPS1,UPS2,UPS3,MBE")]
        public async Task DosPedidos_UnCorreoPorPasada_UnSoloAvisoYUnaSolaEntradaPorAsignar(string orden)
        {
            DosPedidosDeWeelko();

            await UnoPorPasada(EnOrden(orden));

            Assert.AreEqual(0, repositorio.LlamadasAplicar);
            Assert.AreEqual(1, avisos.Count, "Un aviso por envío, no por correo");
            Assert.AreEqual(1, repositorio.Correos.Count(c => c.AvisadoPorAsignar.HasValue));
            Assert.IsTrue(repositorio.Correos.All(c => AsignadorPedidosCompra.Deserializar(c.PedidosCandidatos).Select(x => x.Pedido).OrderBy(p => p)
                .SequenceEqual(new[] { 911500, 912001 })), "Los de UPS heredan los candidatos del de MBE");

            PorAsignarCorreosCompraDTO porAsignar = await PorAsignar();
            EnvioPorAsignarDTO envio = porAsignar.Envios.Single();
            Assert.AreEqual(4, envio.Correos.Count);
            CollectionAssert.IsSubsetOf(new[] { "1Z21E7X76890915153", "1Z21E7X76890089163", "1Z21E7X76893119777" }, envio.Codigos);
            Assert.AreEqual(DIA_UPS, envio.EntregaEstimadaDia);
            Assert.AreEqual(DIA_UPS, envio.FechaRecepcionPropuesta);
            Assert.AreEqual("UPS", envio.Agencia);
            Assert.AreEqual("weelko barcelona s. l", envio.RemitentePaquete);
            Assert.AreEqual(new DateTime(2026, 11, 12, 20, 15, 39), envio.PrimerCorreo);
            CandidatoPorAsignarDTO sugerido = envio.Candidatos.Single(c => c.Sugerido);
            Assert.AreEqual(912001, sugerido.Pedido);
            Assert.AreEqual(new DateTime(2026, 11, 11, 7, 39, 0), sugerido.FechaEnvio);
            CandidatoPorAsignarDTO resto = envio.Candidatos.Single(c => c.Pedido == 911500);
            Assert.AreEqual(3, resto.LineasYaRecibidas);
            Assert.AreEqual(AsignadorPedidosCompra.MOTIVO_NOMBRE, resto.Motivo);
            Assert.IsTrue(porAsignar.PedidosEnAire.Where(p => p.Pedido == 911500 || p.Pedido == 912001).All(p => p.EsCandidato && p.Envios.Single() == envio.Clave));
        }

        [TestMethod]
        public async Task DosPedidos_SeRecibeUno_LosCorreosNuevosDelEnvioNoSeAplicanAlQueQueda()
        {
            // Caso real 13/11/26 (Weelko): el envío por asignar con dos pedidos; a las 10:58 se recibe el bueno (912001) y a las 15:58
            // llega otro correo del mismo envío. Quedaba un solo pedido en el aire (911500) y el job le adelantó la fecha: ya no.
            DosPedidosDeWeelko();
            await Pasada(Envio().Values.ToArray());
            Assert.AreEqual(1, avisos.Count, "por asignar: un aviso");

            repositorio.Recibir(912001, new DateTime(2026, 11, 13, 10, 58, 16));
            CorreoCompras otroDelEnvio = CorreosSeguimientoFase3Tests.Correo(CorreosSeguimientoFase3Tests.MBE_TEXTO, "recogidas061@mbe.es",
                new DateTime(2026, 11, 13, 15, 58, 0), "Sus envíos con MBE 13/11/2026", "mbe-13");
            ahora = new DateTime(2026, 11, 13, 16, 0, 0);
            await Pasada(otroDelEnvio);
            ahora = new DateTime(2026, 11, 13, 16, 30, 0);
            await Pasada();

            Assert.AreEqual(0, repositorio.Rastros.Count, "no se pone fecha a ningún pedido");
            Assert.IsTrue(repositorio.Lineas[911500].All(l => l.FechaRecepcion == new DateTime(2026, 9, 20)), "el que queda en el aire no cambia");
            Assert.IsTrue(repositorio.Correos.All(c => !c.PedidoAplicado.HasValue));
            Assert.AreEqual(1, avisos.Count, "ni un aviso más: el envío ya ha llegado");
            Assert.IsTrue(repositorio.Correos.Where(c => c.Recibido < ahora.Date).All(c => c.PedidoReal == 912001), "la medición dice el bueno");
        }

        [TestMethod]
        public void Decidir_ConUnCorreoMedido_YaLlego()
        {
            var medido = new CorreoSeguimientoFila { Id = 1, Recibido = new DateTime(2026, 11, 12, 20, 15, 0), PedidoReal = 912001 };
            var nuevo = new CorreoSeguimientoFila { Id = 2, Recibido = new DateTime(2026, 11, 13, 15, 58, 0) };
            DecisionEnvio decision = MismoEnvio.Decidir(new[] { medido, nuevo });
            Assert.IsTrue(decision.YaLlego);
            Assert.AreSame(medido, decision.Llegado);
            Assert.IsFalse(MismoEnvio.Decidir(new[] { nuevo }).YaLlego);
        }

        [TestMethod]
        public async Task DosPedidos_EnUnaPasada_UnSoloAviso()
        {
            DosPedidosDeWeelko();

            await Pasada(EnOrden("UPS1,MBE,UPS2,UPS3"));

            Assert.AreEqual(1, avisos.Count);
            Assert.AreEqual(2, avisos[0].Item2);
        }

        [TestMethod]
        public async Task Asignar_UnoDelEnvio_SePropagaALosDemas_ConLaFechaDelEnvioYSiEraElSugerido()
        {
            DosPedidosDeWeelko();
            await Pasada(EnOrden("UPS1,MBE,UPS2,UPS3"));
            int mbe = repositorio.Correos.Single(c => c.Agencia == "UPS" && c.RemitentePaquete == "weelko barcelona s. l").Id;

            ResultadoOperacionCorreoCompra resultado = await Aplicacion().Asignar(mbe,
                new PeticionAsignarCorreoCompraDTO { Empresa = "1", Pedido = 912001, RecordarRemitente = true }, false, @"NUEVAVISION\Compras1");

            Assert.AreEqual(CodigoOperacionCorreoCompra.Ok, resultado.Codigo, resultado.Mensaje);
            RespuestaAsignacionCorreoCompraDTO r = resultado.Respuesta;
            Assert.AreEqual(3, r.CorreosDelMismoEnvio);
            Assert.AreEqual(2, r.LineasCambiadas);
            Assert.AreEqual(DIA_UPS, r.FechaNueva);
            CollectionAssert.AreEqual(new DateTime?[] { null, new DateTime(2026, 11, 20) }, r.FechasAnteriores);
            Assert.AreEqual(true, r.EligioSugerido);
            Assert.IsTrue(repositorio.Correos.All(c => c.PedidoAplicado == 912001 && c.EligioSugerido == true));
            Assert.AreEqual(1, repositorio.Rastros.Count);
            Assert.IsTrue(repositorio.Rastros.Single().EligioSugerido == true);
            Assert.AreEqual(new DateTime(2026, 9, 20), repositorio.Lineas[911500].Single().FechaRecepcion, "El otro pedido no se toca");
            Assert.IsTrue(r.RemitenteRecordado);
            Assert.AreEqual("9100", repositorio.RemitentesPaquete.Single(x => x.RemitentePaquete == "weelko barcelona s. l").Proveedor);
            StringAssert.Contains(r.Mensaje, "3 correo(s) más del mismo envío");
        }

        [TestMethod]
        public async Task Asignar_ElNoSugerido_LoApuntaParaMedir()
        {
            DosPedidosDeWeelko();
            await Pasada(EnOrden("UPS1,MBE"));

            ResultadoOperacionCorreoCompra resultado = await Aplicacion().Asignar(1, new PeticionAsignarCorreoCompraDTO { Pedido = 911500 }, false, "Prueba");

            Assert.AreEqual(false, resultado.Respuesta.EligioSugerido);
            Assert.AreEqual(false, repositorio.Rastros.Single().EligioSugerido);
        }

        [TestMethod]
        public async Task Asignar_PedidoQueNoEstaEnElAire_NoEnElAire_YCorreoQueNoExiste()
        {
            DosPedidosDeWeelko();
            await Pasada(EnOrden("UPS1"));

            Assert.AreEqual(CodigoOperacionCorreoCompra.NoEnElAire, (await Aplicacion().Asignar(1, new PeticionAsignarCorreoCompraDTO { Pedido = 999999 }, false, "Prueba")).Codigo);
            Assert.AreEqual(CodigoOperacionCorreoCompra.NoExiste, (await Aplicacion().Asignar(77, new PeticionAsignarCorreoCompraDTO { Pedido = 912001 }, false, "Prueba")).Codigo);
            Assert.AreEqual(CodigoOperacionCorreoCompra.PeticionIncorrecta, (await Aplicacion().Asignar(1, new PeticionAsignarCorreoCompraDTO(), false, "Prueba")).Codigo);
        }

        [TestMethod]
        public async Task Asignar_OtraVezAlMismo_NoTocaNada_YAOtroEs409SalvoReasignar()
        {
            DosPedidosDeWeelko();
            await Pasada(EnOrden("UPS1"));
            ServicioAplicacionCorreosCompras servicio = Aplicacion();
            _ = await servicio.Asignar(1, new PeticionAsignarCorreoCompraDTO { Pedido = 912001 }, false, "Prueba");

            ResultadoOperacionCorreoCompra otraVez = await servicio.Asignar(1, new PeticionAsignarCorreoCompraDTO { Pedido = 912001 }, false, "Prueba");
            Assert.AreEqual(CodigoOperacionCorreoCompra.Ok, otraVez.Codigo);
            Assert.IsTrue(otraVez.Respuesta.YaEstabaAplicado);
            Assert.AreEqual(1, repositorio.Rastros.Count);

            ResultadoOperacionCorreoCompra aOtro = await servicio.Asignar(1, new PeticionAsignarCorreoCompraDTO { Pedido = 911500 }, false, "Prueba");
            Assert.AreEqual(CodigoOperacionCorreoCompra.Conflicto, aOtro.Codigo);

            ResultadoOperacionCorreoCompra reasignado = await servicio.Asignar(1, new PeticionAsignarCorreoCompraDTO { Pedido = 911500 }, true, "Prueba");
            Assert.AreEqual(CodigoOperacionCorreoCompra.Ok, reasignado.Codigo, reasignado.Mensaje);
            Assert.AreEqual(912001, reasignado.Respuesta.PedidoAnterior);
            Assert.AreEqual(911500, repositorio.Correo(1).PedidoAplicado);
            Assert.IsTrue(repositorio.Lineas[912001].All(l => l.FechaRecepcion == DIA_UPS), "La fecha del anterior no se deshace");
            Assert.AreEqual(DIA_UPS, repositorio.Lineas[911500].Single().FechaRecepcion);
        }

        [TestMethod]
        public async Task Asignar_ConElInterruptorApagado_SiFunciona()
        {
            UnPedidoDeWeelko();
            interruptor = false;
            await Pasada(EnOrden("UPS1"));

            ResultadoOperacionCorreoCompra resultado = await Aplicacion().Asignar(1, new PeticionAsignarCorreoCompraDTO { Pedido = 912001 }, false, "Prueba");

            Assert.AreEqual(CodigoOperacionCorreoCompra.Ok, resultado.Codigo);
            Assert.AreEqual("Prueba", repositorio.Correo(1).AplicadoPor);
        }

        [TestMethod]
        public async Task Descartar_SePropagaAlEnvio_YLoQueLlegaDespuesTambienSinAviso()
        {
            DosPedidosDeWeelko();
            await Pasada(EnOrden("UPS1,MBE,UPS2"));
            Assert.AreEqual(1, avisos.Count);

            ResultadoOperacionCorreoCompra resultado = await Aplicacion().Descartar(1, "Prueba");
            Assert.AreEqual(CodigoOperacionCorreoCompra.Ok, resultado.Codigo);
            Assert.AreEqual(2, resultado.CorreosDelMismoEnvio);

            await Pasada(EnOrden("UPS3"));

            Assert.IsTrue(repositorio.Correos.All(c => c.Descartado == true));
            Assert.AreEqual(1, avisos.Count);
            Assert.AreEqual(0, (await PorAsignar()).Envios.Count);
        }

        [TestMethod]
        public async Task Descartar_UnoYaAplicado_409()
        {
            UnPedidoDeWeelko();
            await Pasada(EnOrden("UPS1,MBE"));

            Assert.AreEqual(CodigoOperacionCorreoCompra.Conflicto, (await Aplicacion().Descartar(1, "Prueba")).Codigo);
        }

        [TestMethod]
        public async Task CodigosDistintos_NoSeEnlazan_DosAvisos()
        {
            DosPedidosDeWeelko();

            await Pasada(Ups("a", "1Z21E7X76890915153", 20, 0, 0));
            await Pasada(Ups("b", "1Z999AA10123456784", 20, 5, 0));

            Assert.AreEqual(2, avisos.Count);
            Assert.AreEqual(2, (await PorAsignar()).Envios.Count);
        }

        #endregion

        #region Reanálisis

        [TestMethod]
        public async Task Reanalizar_RehaceElAnalisisYLosEnlacesDeLoYaGuardado()
        {
            DosPedidosDeWeelko();
            CorreoCompras[] correos = EnOrden("UPS1,MBE,UPS2,UPS3");
            // Lo guardado antes de publicar: sin remitente del paquete, el de MBE con un solo código y nadie con candidatos
            foreach (CorreoCompras c in correos)
            {
                repositorio.Correos.Add(new CorreoSeguimientoFila { Id = repositorio.Correos.Count + 1, MensajeId = c.Clave(), Recibido = c.Recibido, Agencia = "UPS", Codigo = "1Z21E7X76890915153" });
            }
            repositorio.Correos[1].Codigo = "ES0061-0L-000000VTHP";
            A.CallTo(() => lector.LeerPagina(A<string>._, A<DateTime>._, A<DateTime>._))
                .Returns(Task.FromResult(new LecturaBuzonCompras { Correos = correos.ToList() }));

            ResultadoReanalisisCorreos resultado = await Nucleo().Reanalizar(7, AHORA);

            Assert.AreEqual(EstadoPasadaCorreosSeguimiento.Hecha, resultado.Estado);
            Assert.AreEqual(4, resultado.Leidos);
            Assert.AreEqual(4, resultado.Actualizados);
            Assert.AreEqual(3, resultado.Enlazados, "Los tres de UPS heredan los candidatos del de MBE");
            Assert.AreEqual(0, repositorio.LlamadasAplicar, "El reanálisis no aplica");
            Assert.AreEqual(0, avisos.Count, "Ni avisa");
            Assert.AreEqual(1, (await PorAsignar()).Envios.Count);
            A.CallTo(() => lector.LeerNuevos(A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        #endregion

        private async Task<PorAsignarCorreosCompraDTO> PorAsignar() => ComprasAgenciasEntradaController.ComponerPorAsignar(
            await repositorio.LeerCorreos(AHORA.AddDays(-17)), await repositorio.LeerPedidosEnAire("1"), AHORA.Date.AddDays(-7), AHORA.Date);
    }
}
