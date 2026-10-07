using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#600: todo lo que depende del tipo de entrada o de salida (títulos, detalle, textos de la confirmación, qué
    /// va a Ubicar, packing…) lo dice su estrategia y el núcleo lo copia a los DTO. Ariadna y Nesto no miran el código del
    /// tipo: un tipo nuevo se añade tocando solo la API.
    /// </summary>
    [TestClass]
    public class TextosTipoAlmacenTests
    {
        private const string EMPRESA = "1";

        private static IPrincipal Usuario()
        {
            return new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Andre") }, "prueba"));
        }

        #region Recepciones: cada estrategia

        [TestMethod]
        public void Compras_DetalleConProveedorPedidosUnidadesYFecha()
        {
            var textos = new OrigenRecepcionCompras.TextosCompras();

            string detalle = textos.Detalle(new RecepcionPendienteDTO
            {
                Tipo = "COMP", Documento = "65", Pedidos = new List<int> { 220401, 220433 }, Lineas = 5, Unidades = 40, Fecha = new DateTime(2026, 10, 1)
            });

            Assert.AreEqual("Proveedor 65 · 2 pedidos · 40 ud. · 01/10", detalle);
        }

        [TestMethod]
        public void Compras_ConfirmacionPorCasoYQueVaAUbicarSegunHayaAlbaranes()
        {
            var textos = new OrigenRecepcionCompras.TextosCompras();

            StringAssert.Contains(textos.AvisoCoincide, "entra en los pedidos del proveedor");
            Assert.AreEqual(textos.AvisoCoincide, textos.AvisoNoCoincide);
            StringAssert.Contains(textos.AvisoConFaltas, "Lo que falta sigue pendiente");
            StringAssert.Contains(textos.AvisoConRecuperadas, "no servido hace poco");
            StringAssert.Contains(textos.AvisoConSobras, "visto bueno de Compras");
            StringAssert.Contains(textos.AvisoConAjenos, "se avisa a Compras");
            StringAssert.StartsWith(textos.AvisoUbicar(new ResultadoTerminarRecepcionDTO()), "Todavía no aparece en Ubicar");
            Assert.AreEqual("Lo que ha entrado en los albaranes ya aparece en Ubicar.", textos.AvisoUbicar(new ResultadoTerminarRecepcionDTO
            {
                Documentos = new List<DocumentoRecepcionDTO> { new DocumentoRecepcionDTO { Pedido = 1, Albaran = 2 } }
            }));
        }

        [TestMethod]
        public void Reposiciones_TextosPropiosYDetalleGenerico()
        {
            var textos = new OrigenRecepcionReposiciones.TextosReposiciones();

            Assert.AreEqual("¿Terminar la reposición 80905 con esto?", textos.TituloConfirmacion(new RecepcionDTO { Documento = "80905" }));
            StringAssert.StartsWith(textos.AvisoCoincide, "Entra la reposición entera");
            StringAssert.StartsWith(textos.AvisoNoCoincide, "Lo leído no coincide con lo enviado");
            Assert.IsNull(textos.AvisoConFaltas, "En una reposición basta con decir que no coincide");
            Assert.IsNull(textos.AvisoConAjenos);
            Assert.AreEqual("Lo recibido ya aparece en Ubicar.", textos.AvisoUbicar(new ResultadoTerminarRecepcionDTO()));
            Assert.AreEqual("1 línea · 1 ud. · 06/10", textos.Detalle(new RecepcionPendienteDTO { Lineas = 1, Unidades = 1, Fecha = new DateTime(2026, 10, 6) }));
        }

        [TestMethod]
        public void Estrategias_DeclaranSusTextos()
        {
            Assert.IsInstanceOfType(new OrigenRecepcionCompras(null, null).Textos, typeof(OrigenRecepcionCompras.TextosCompras));
            Assert.IsInstanceOfType(new OrigenRecepcionReposiciones(null, null, null).Textos, typeof(OrigenRecepcionReposiciones.TextosReposiciones));
            Assert.IsInstanceOfType(new OrigenSalidaPicking(A.Fake<IRepositorioPreparacionAlmacen>()).Textos, typeof(OrigenSalidaPicking.TextosPicking));
            Assert.IsInstanceOfType(new OrigenSalidaReposicion(A.Fake<IRepositorioPreparacionAlmacen>()).Textos, typeof(OrigenSalidaReposicion.TextosReposicion));
        }

        #endregion

        #region Recepciones: el núcleo los copia

        private static IOrigenRecepcion OrigenRecepcion(string tipo, TextosTipoRecepcion textos)
        {
            IOrigenRecepcion origen = A.Fake<IOrigenRecepcion>();
            A.CallTo(() => origen.Tipo).Returns(tipo);
            A.CallTo(() => origen.SeTerminaDesdeAqui).Returns(true);
            A.CallTo(() => origen.PuedeTerminar(A<IPrincipal>._, A<string>._, A<string>._)).Returns(true);
            A.CallTo(() => origen.Textos).Returns(textos);
            return origen;
        }

        [TestMethod]
        public async Task Nucleo_LeerPendientes_PoneElDetalleDelTipoYRespetaElTitulo()
        {
            IOrigenRecepcion compras = OrigenRecepcion("COMP", new OrigenRecepcionCompras.TextosCompras());
            A.CallTo(() => compras.LeerPendientes(EMPRESA, "ALG")).Returns(new List<RecepcionPendienteDTO>
            {
                new RecepcionPendienteDTO { Tipo = "COMP", Documento = "65", Titulo = "MAYSTAR", Pedidos = new List<int> { 1 }, Unidades = 40 }
            });

            RecepcionPendienteDTO pendiente = (await new ServicioRecepciones(new[] { compras }).LeerPendientes(EMPRESA, "ALG")).Single();

            Assert.AreEqual("MAYSTAR", pendiente.Titulo);
            Assert.AreEqual("Proveedor 65 · 1 pedido · 40 ud.", pendiente.Detalle);
        }

        [TestMethod]
        public async Task Nucleo_Buscar_TambienLosCompleta()
        {
            IOrigenRecepcion reposiciones = OrigenRecepcion("REPO", new OrigenRecepcionReposiciones.TextosReposiciones());
            A.CallTo(() => reposiciones.BuscarPorCodigo(EMPRESA, "ALG", "22624")).Returns(new List<RecepcionPendienteDTO>
            {
                new RecepcionPendienteDTO { Tipo = "REPO", Documento = "80905", Titulo = "Reposición 80905 desde Alcobendas", Lineas = 1, Unidades = 1 }
            });

            RecepcionPendienteDTO encontrada = (await new ServicioRecepciones(new[] { reposiciones }).Buscar(EMPRESA, "ALG", "22624")).Single();

            Assert.AreEqual("1 línea · 1 ud.", encontrada.Detalle);
        }

        [TestMethod]
        public async Task Nucleo_LeerEsperado_CopiaLosTextosDeLaConfirmacion()
        {
            IOrigenRecepcion compras = OrigenRecepcion("COMP", new OrigenRecepcionCompras.TextosCompras());
            A.CallTo(() => compras.LeerEsperado(EMPRESA, "ALG", "65")).Returns(new RecepcionDTO { Tipo = "COMP", Documento = "65", Titulo = "MAYSTAR" });

            RecepcionDTO recepcion = await new ServicioRecepciones(new[] { compras }).LeerEsperado("COMP", EMPRESA, "ALG", "65", Usuario());

            var textos = new OrigenRecepcionCompras.TextosCompras();
            Assert.AreEqual("¿Terminar la recepción con esto?", recepcion.TituloConfirmacion);
            Assert.AreEqual(textos.AvisoCoincide, recepcion.AvisoCoincide);
            Assert.AreEqual(textos.AvisoNoCoincide, recepcion.AvisoNoCoincide);
            Assert.AreEqual(textos.AvisoConFaltas, recepcion.AvisoConFaltas);
            Assert.AreEqual(textos.AvisoConSobras, recepcion.AvisoConSobras);
            Assert.AreEqual(textos.AvisoConRecuperadas, recepcion.AvisoConRecuperadas);
            Assert.AreEqual(textos.AvisoConAjenos, recepcion.AvisoConAjenos);
        }

        [TestMethod]
        public async Task Nucleo_Terminar_MontaElMensajeEnteroConLoQueVaAUbicar()
        {
            IOrigenRecepcion compras = OrigenRecepcion("COMP", new OrigenRecepcionCompras.TextosCompras());
            A.CallTo(() => compras.Terminar(A<SolicitudTerminarRecepcion>._)).Returns(new ResultadoTerminarRecepcionDTO
            {
                Tipo = "COMP",
                Documento = "65",
                Documentos = new List<DocumentoRecepcionDTO> { new DocumentoRecepcionDTO { Pedido = 220401, Albaran = 5600 } },
                NoEsperados = new List<DiferenciaPreparacionDTO> { new DiferenciaPreparacionDTO { Producto = "X", Leido = 2, Ajeno = true } },
                Avisos = new List<string> { "Compras está avisado de lo que no estaba pedido." }
            });

            ResultadoTerminarRecepcionDTO resultado = await new ServicioRecepciones(new[] { compras }).Terminar("COMP", EMPRESA, "ALG", "65",
                new TerminarRecepcionDTO { IdRecepcion = Guid.NewGuid(), Lecturas = new List<LecturaRecepcionDTO> { new LecturaRecepcionDTO { Producto = "A", Cantidad = 1 } } },
                Usuario());

            Assert.AreEqual("Lo que ha entrado en los albaranes ya aparece en Ubicar.", resultado.AvisoUbicar);
            Assert.AreEqual(string.Join(Environment.NewLine,
                "Recibido. albarán 5600 (pedido 220401).",
                "No han entrado (no se esperaban): X (2).",
                "Compras está avisado de lo que no estaba pedido.",
                "Lo que ha entrado en los albaranes ya aparece en Ubicar."), resultado.Mensaje);
        }

        [TestMethod]
        public async Task Nucleo_Terminar_YaTerminada_NoDiceNadaDeUbicar()
        {
            IOrigenRecepcion reposiciones = OrigenRecepcion("REPO", new OrigenRecepcionReposiciones.TextosReposiciones());
            A.CallTo(() => reposiciones.Terminar(A<SolicitudTerminarRecepcion>._))
                .Returns(new ResultadoTerminarRecepcionDTO { Tipo = "REPO", Documento = "80905", YaEstabaTerminada = true });

            ResultadoTerminarRecepcionDTO resultado = await new ServicioRecepciones(new[] { reposiciones }).Terminar("REPO", EMPRESA, "ALG", "80905",
                new TerminarRecepcionDTO { IdRecepcion = Guid.NewGuid(), Lecturas = new List<LecturaRecepcionDTO> { new LecturaRecepcionDTO { Producto = "A", Cantidad = 1 } } },
                Usuario());

            Assert.IsNull(resultado.AvisoUbicar);
            Assert.AreEqual("Esta recepción ya estaba terminada: no se ha repetido nada.", resultado.Mensaje);
        }

        [TestMethod]
        public void Nucleo_MensajeTerminada_DiferenciasDeUnaReposicion()
        {
            string mensaje = ServicioRecepciones.MensajeTerminada(new ResultadoTerminarRecepcionDTO
            {
                Diferencias = new List<DiferenciaPreparacionDTO>
                {
                    new DiferenciaPreparacionDTO { Producto = "A", Esperado = 3, Leido = 2 },
                    new DiferenciaPreparacionDTO { Producto = "B", Leido = 1, Ajeno = true }
                },
                AvisoUbicar = "Lo recibido ya aparece en Ubicar."
            });

            Assert.AreEqual(string.Join(Environment.NewLine,
                "Recepción terminada.",
                "No coincide con lo enviado:",
                "  A: enviado 3, leído 2 (falta 1)",
                "  B: no venía, leído 1",
                "Lo recibido ya aparece en Ubicar."), mensaje);
        }

        #endregion

        #region Un tipo de recepción nuevo sale completo sin tocar nada más

        /// <summary>Un tipo inventado (devoluciones de clientes) que solo dice lo suyo: lo demás, genérico.</summary>
        private class OrigenDevolucionesDePrueba : IOrigenRecepcion
        {
            public string Tipo => "DEVO";
            public bool SeTerminaDesdeAqui => true;
            public TextosTipoRecepcion Textos { get; } = new TextosDevoluciones();

            private class TextosDevoluciones : TextosTipoRecepcion
            {
                public override string AvisoCoincide => "Entra la devolución entera y se hace la rectificativa.";
                public override string AvisoUbicar(ResultadoTerminarRecepcionDTO resultado) => "La devolución ya aparece en Ubicar.";
            }

            public bool PuedeTerminar(IPrincipal usuario, string empresa, string almacen) => true;
            public Task<List<RecepcionPendienteDTO>> BuscarPorCodigo(string empresa, string almacen, string codigo) => LeerPendientes(empresa, almacen);
            public Task<List<RecepcionPendienteDTO>> LeerPendientes(string empresa, string almacen) => Task.FromResult(new List<RecepcionPendienteDTO>
            {
                new RecepcionPendienteDTO { Tipo = Tipo, Documento = "123", Lineas = 2, Unidades = 3 }
            });
            public Task<RecepcionDTO> LeerEsperado(string empresa, string almacen, string documento) =>
                Task.FromResult(new RecepcionDTO { Tipo = Tipo, Documento = documento, Lineas = new List<LineaRecepcionDTO>() });
            public Task<ResultadoTerminarRecepcionDTO> Terminar(SolicitudTerminarRecepcion solicitud) =>
                Task.FromResult(new ResultadoTerminarRecepcionDTO { Tipo = Tipo, Documento = solicitud.Documento });
        }

        [TestMethod]
        public async Task TipoNuevoDeRecepcion_SaleConTodosSusTextos()
        {
            var servicio = new ServicioRecepciones(new IOrigenRecepcion[] { new OrigenDevolucionesDePrueba() });

            RecepcionPendienteDTO pendiente = (await servicio.LeerPendientes(EMPRESA, "ALG")).Single();
            RecepcionDTO recepcion = await servicio.LeerEsperado("DEVO", EMPRESA, "ALG", "123", Usuario());
            ResultadoTerminarRecepcionDTO resultado = await servicio.Terminar("DEVO", EMPRESA, "ALG", "123",
                new TerminarRecepcionDTO { IdRecepcion = Guid.NewGuid(), Lecturas = new List<LecturaRecepcionDTO> { new LecturaRecepcionDTO { Producto = "A", Cantidad = 1 } } },
                Usuario());

            Assert.AreEqual("DEVO 123", pendiente.Titulo);
            Assert.AreEqual("2 líneas · 3 ud.", pendiente.Detalle);
            Assert.AreEqual("DEVO 123", recepcion.Titulo);
            Assert.AreEqual("¿Terminar la recepción con esto?", recepcion.TituloConfirmacion);
            Assert.AreEqual("Entra la devolución entera y se hace la rectificativa.", recepcion.AvisoCoincide);
            Assert.AreEqual("Lo leído no coincide con lo esperado.", recepcion.AvisoNoCoincide);
            Assert.IsNull(recepcion.AvisoConFaltas);
            Assert.AreEqual("La devolución ya aparece en Ubicar.", resultado.AvisoUbicar);
            Assert.AreEqual("Recepción terminada." + Environment.NewLine + "La devolución ya aparece en Ubicar.", resultado.Mensaje);
        }

        [TestMethod]
        public async Task TipoDeRecepcionSinTextos_SaleConLosGenericos()
        {
            IOrigenRecepcion origen = OrigenRecepcion("DEVO", null);
            A.CallTo(() => origen.LeerEsperado(EMPRESA, "ALG", "123")).Returns(new RecepcionDTO { Tipo = "DEVO", Documento = "123" });

            RecepcionDTO recepcion = await new ServicioRecepciones(new[] { origen }).LeerEsperado("DEVO", EMPRESA, "ALG", "123", Usuario());

            Assert.AreEqual("DEVO 123", recepcion.Titulo);
            Assert.AreEqual(TextosTipoRecepcion.Genericos.AvisoCoincide, recepcion.AvisoCoincide);
            Assert.AreEqual(TextosTipoRecepcion.Genericos.AvisoNoCoincide, recepcion.AvisoNoCoincide);
        }

        #endregion

        #region Salidas

        private static ServicioSalidas Salidas(IRepositorioPreparacionAlmacen repositorio, params IOrigenSalida[] otros)
        {
            return new ServicioSalidas(new IOrigenSalida[] { new OrigenSalidaPicking(repositorio), new OrigenSalidaReposicion(repositorio) }.Concat(otros));
        }

        private static LineaPickingAlmacenDTO Linea(string producto, int cantidad)
            => new LineaPickingAlmacenDTO { Producto = producto, Descripcion = "Producto " + producto, Cantidad = cantidad, Pasillo = "001", Fila = "001", Columna = "001" };

        [TestMethod]
        public async Task Salidas_LeerPendientes_TituloDetalleYPackingDeCadaTipo()
        {
            IRepositorioPreparacionAlmacen repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            A.CallTo(() => repositorio.LeerPickingsEnCurso(EMPRESA, "ALG")).Returns(new List<PickingEnCursoDTO>
            {
                new PickingEnCursoDTO { Picking = 99757, Lineas = 12, Pedidos = 4, Unidades = 30 }
            });
            A.CallTo(() => repositorio.LeerReposicionesPorSalir(EMPRESA, "ALG")).Returns(new List<ReposicionPorSalir>
            {
                new ReposicionPorSalir { Traspaso = 80905, Destino = "REI", NombreDestino = "Reina", Lineas = 1, Unidades = 1 },
                new ReposicionPorSalir { Traspaso = 80906, Destino = "ALC", Lineas = 2, Unidades = 5 }
            });

            List<RecogidaPendienteDTO> pendientes = await Salidas(repositorio).LeerPendientes(EMPRESA, "ALG");

            RecogidaPendienteDTO picking = pendientes.Single(p => p.Numero == 99757);
            Assert.AreEqual("Picking 99757", picking.Titulo);
            Assert.AreEqual("4 pedidos · 12 líneas · 30 uds → Mesa de packing", picking.Detalle);
            Assert.IsTrue(picking.TienePacking);
            RecogidaPendienteDTO reina = pendientes.Single(p => p.Numero == 80905);
            Assert.AreEqual("Reposición 80905 hacia Reina", reina.Titulo, "El nombre del almacén lo pone el servidor");
            Assert.AreEqual("Reina", reina.NombreDestino);
            Assert.AreEqual("REI", reina.Destino, "El código sigue viniendo, como antes");
            Assert.AreEqual("1 línea · 1 ud", reina.Detalle);
            Assert.IsFalse(reina.TienePacking);
            Assert.AreEqual("Reposición 80906 hacia ALC", pendientes.Single(p => p.Numero == 80906).Titulo, "Sin nombre, el código");
        }

        [TestMethod]
        public async Task Salidas_LeerRecogida_UnaReposicion_TextosDeLaConfirmacion()
        {
            IRepositorioPreparacionAlmacen repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            A.CallTo(() => repositorio.LeerReposicionSalida(EMPRESA, 80905)).Returns(new ReposicionSalida
            {
                Destino = "REI", NombreDestino = "Reina", Lineas = new List<LineaPickingAlmacenDTO> { Linea("A", 1) }
            });

            RecogidaAlmacenDTO recogida = await Salidas(repositorio).LeerRecogida(EMPRESA, "REPO", 80905);

            Assert.AreEqual("Reposición 80905 hacia Reina", recogida.Titulo);
            Assert.AreEqual("¿Terminar la reposición 80905?", recogida.TituloConfirmacion);
            Assert.AreEqual("Sin faltas.", recogida.AvisoSinFaltas);
            StringAssert.StartsWith(recogida.AvisoConFaltas, "No salen");
            Assert.AreEqual("Se contabiliza la salida del traspaso hacia Reina: la mercancía deja de estar en este almacén y no se puede deshacer desde Ariadna.",
                recogida.AvisoAlTerminar);
            Assert.AreEqual("Esta reposición ya está terminada.", recogida.AvisoCerrada);
            Assert.IsFalse(recogida.TienePacking);
            Assert.IsFalse(recogida.PermiteCambiarHueco);
        }

        [TestMethod]
        public async Task Salidas_LeerRecogida_UnPicking_TienePackingYOtroHueco()
        {
            IRepositorioPreparacionAlmacen repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            A.CallTo(() => repositorio.LeerLineasPicking(EMPRESA, 99757)).Returns(new List<LineaPickingAlmacenDTO> { Linea("A", 1) });

            RecogidaAlmacenDTO recogida = await Salidas(repositorio).LeerRecogida(EMPRESA, "PICK", 99757);

            Assert.AreEqual("Picking 99757", recogida.Titulo);
            Assert.AreEqual("¿Terminar el picking 99757?", recogida.TituloConfirmacion);
            StringAssert.StartsWith(recogida.AvisoConFaltas, "Se quitan de sus pedidos");
            Assert.IsNull(recogida.AvisoAlTerminar);
            StringAssert.Contains(recogida.AvisoCerrada, "Packing");
            Assert.AreEqual("Mesa de packing", recogida.NombreDestino);
            Assert.IsTrue(recogida.TienePacking);
            Assert.IsTrue(recogida.PermiteCambiarHueco);
        }

        /// <summary>Un tipo de salida inventado que no dice nada propio: todo genérico.</summary>
        private class OrigenSalidaDePrueba : IOrigenSalida
        {
            public string Tipo => "DEVP";
            public TextosTipoSalida Textos => null;
            public bool SeTerminaDesdeAqui => true;
            public bool PuedeTerminar(IPrincipal usuario, string empresa) => true;
            public Task<List<RecogidaPendienteDTO>> LeerPendientes(string empresa, string almacen) => Task.FromResult(new List<RecogidaPendienteDTO>
            {
                new RecogidaPendienteDTO { Tipo = Tipo, Numero = 7, Destino = "PROV", NombreDestino = "Proveedor 65", Lineas = 2, Unidades = 3 }
            });
            public Task<RecorridoSalida> LeerRecorrido(string empresa, int numero) => Task.FromResult(new RecorridoSalida
            {
                Destino = "PROV", NombreDestino = "Proveedor 65", Lineas = new List<LineaPickingAlmacenDTO> { Linea("A", 1) }
            });
            public Task<ResultadoTerminarSalidaDTO> Terminar(string empresa, int numero, EstadoPickingDTO estado, IPrincipal usuario, ITransaccionSalida tx) =>
                throw new NotImplementedException();
            public Task<FotoSalida> PrepararFoto(ITransaccionSalida tx, string empresa, int numero) => throw new NotImplementedException();
        }

        [TestMethod]
        public async Task TipoNuevoDeSalida_SaleConTodosSusTextos()
        {
            ServicioSalidas servicio = Salidas(A.Fake<IRepositorioPreparacionAlmacen>(), new OrigenSalidaDePrueba());

            RecogidaPendienteDTO pendiente = (await servicio.LeerPendientes(EMPRESA, "ALG")).Single(p => p.Tipo == "DEVP");
            RecogidaAlmacenDTO recogida = await servicio.LeerRecogida(EMPRESA, "DEVP", 7);

            Assert.AreEqual("DEVP 7 hacia Proveedor 65", pendiente.Titulo);
            Assert.AreEqual("2 líneas · 3 uds", pendiente.Detalle);
            Assert.IsFalse(pendiente.TienePacking);
            Assert.AreEqual("DEVP 7 hacia Proveedor 65", recogida.Titulo);
            Assert.AreEqual("¿Terminar DEVP 7?", recogida.TituloConfirmacion);
            Assert.AreEqual("Sin faltas.", recogida.AvisoSinFaltas);
            Assert.AreEqual("Lo que falta no sale.", recogida.AvisoConFaltas);
            Assert.IsNull(recogida.AvisoAlTerminar);
            Assert.AreEqual("DEVP 7 ya está terminada.", recogida.AvisoCerrada);
        }

        [TestMethod]
        public async Task TipoDeSalidaFalsoDeFakeItEasy_TextosEnBlanco_SalenLosGenericos()
        {
            IOrigenSalida origen = A.Fake<IOrigenSalida>();
            A.CallTo(() => origen.Tipo).Returns("DEVP");
            A.CallTo(() => origen.LeerRecorrido(EMPRESA, 7)).Returns(new RecorridoSalida { Lineas = new List<LineaPickingAlmacenDTO> { Linea("A", 1) } });

            RecogidaAlmacenDTO recogida = await new ServicioSalidas(new[] { origen }).LeerRecogida(EMPRESA, "DEVP", 7);

            Assert.AreEqual("DEVP 7", recogida.Titulo);
            Assert.AreEqual("¿Terminar DEVP 7?", recogida.TituloConfirmacion);
            Assert.AreEqual("Lo que falta no sale.", recogida.AvisoConFaltas);
        }

        #endregion
    }
}
