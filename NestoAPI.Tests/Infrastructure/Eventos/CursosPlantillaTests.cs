using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net.Mail;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Eventos
{
    /// <summary>
    /// NestoAPI#691 (Nesto#543): cursos desde la plantilla. Separación en dos pedidos (nunca series mezcladas), precio por
    /// modalidad, los cursos fuera de portes y Ganavisiones (100 € de productos + 150 € de curso → 100 €), la inscripción ligada
    /// al pedido del curso, el endpoint PorProducto y la búsqueda.
    /// </summary>
    [TestClass]
    public class CursosPlantillaTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 10, 22, 10, 0, 0);
        private const int EVENTO = 123;
        private const int EVENTO_LLENO = 124;
        private const int EVENTO_ONLINE = 125;

        private NVEntities db;
        private List<Evento> eventos;
        private List<EventoInscripcion> inscripciones;
        private List<Producto> productos;
        private List<Cliente> clientes;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            eventos = new List<Evento>
            {
                new Evento { Id = EVENTO, Empresa = "1", Titulo = "Curso de cejas", Fecha = new DateTime(2026, 11, 10), HoraInicio = new TimeSpan(10, 0, 0),
                    ImporteSenal = 100, ImporteSenalNoReembolsable = 60, Activo = true, Almacen = "REI", ProductoCurso = "90077", Plazas = 10 },
                new Evento { Id = EVENTO_LLENO, Empresa = "1", Titulo = "Curso lleno", Fecha = new DateTime(2026, 11, 3),
                    ImporteSenal = 100, Activo = true, Almacen = "ALC", ProductoCurso = "90077", Plazas = 1 },
                new Evento { Id = EVENTO_ONLINE, Empresa = "1", Titulo = "Webinar", Fecha = new DateTime(2026, 11, 1), Formato = "Online",
                    ImporteSenal = 150, Activo = true, Almacen = "ALC", ProductoCurso = "90077" },
                new Evento { Id = 126, Empresa = "1", Titulo = "Pasado", Fecha = new DateTime(2026, 10, 1), ImporteSenal = 100, Activo = true, Almacen = "REI", ProductoCurso = "90077" },
                new Evento { Id = 127, Empresa = "1", Titulo = "Inactivo", Fecha = new DateTime(2026, 12, 1), ImporteSenal = 100, Activo = false, Almacen = "REI", ProductoCurso = "90077" },
                new Evento { Id = 128, Empresa = "1", Titulo = "Gratuito", Fecha = new DateTime(2026, 12, 1), Activo = true, Almacen = "REI", ProductoCurso = "90077" }
            };
            inscripciones = new List<EventoInscripcion>
            {
                new EventoInscripcion { Id = 1, EventoId = EVENTO_LLENO, Empresa = "1", Cliente = "20000", Contacto = "0", Estado = "Pagada" }
            };
            productos = new List<Producto>
            {
                new Producto { Empresa = "1", Número = "90077", Nombre = "CURSO FORMACIÓN", Grupo = "CUR", Ficticio = true, PVP = 100, IVA_Repercutido = "EX", Estado = 0 },
                new Producto { Empresa = "1", Número = "12345", Nombre = "CREMA", Grupo = "COS", PVP = 10, IVA_Repercutido = "G21", Estado = 0 }
            };
            clientes = new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0  ", Nombre = "LUNA", IVA = "G21", Vendedor = "JE ", Ruta = "AT ", ClientePrincipal = true }
            };
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            A.CallTo(() => db.EventosInscripciones).Returns(Crear(inscripciones));
            A.CallTo(() => db.Productos).Returns(Crear(productos));
            A.CallTo(() => db.Clientes).Returns(Crear(clientes));
        }

        private ServicioCursosPlantilla Servicio(IServicioInscripcionesEventos inscripcionesServicio = null, ICreadorPedidoCursoPlantilla creador = null,
            IAvisadorCursoPlantilla avisador = null) =>
            new ServicioCursosPlantilla(db, () => HOY, new CatalogoCursosPlantilla(db, () => HOY), () => inscripcionesServicio,
                creador ?? A.Fake<ICreadorPedidoCursoPlantilla>(), avisador ?? A.Fake<IAvisadorCursoPlantilla>());

        private static LineaPedidoVentaDTO Producto(string producto, decimal precio, short cantidad = 1) => new LineaPedidoVentaDTO
        {
            tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            Producto = producto,
            Cantidad = cantidad,
            PrecioUnitario = precio,
            GrupoProducto = "COS",
            almacen = "ALG",
            formaVenta = "DIR"
        };

        private static LineaPedidoVentaDTO Curso(int? evento = EVENTO, string modalidad = "Reembolsable", decimal precio = 150) => new LineaPedidoVentaDTO
        {
            tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            Producto = "90077",
            Cantidad = 1,
            PrecioUnitario = precio,
            EventoId = evento,
            ModalidadEvento = modalidad,
            almacen = "ALG",
            formaVenta = "DIR"
        };

        private static PedidoVentaDTO Pedido(params LineaPedidoVentaDTO[] lineas)
        {
            var pedido = new PedidoVentaDTO
            {
                empresa = "1",
                cliente = "15191",
                contacto = "0",
                iva = "G21",
                formaPago = "TAR",
                plazosPago = "PRE",
                Usuario = "NUEVAVISION\\Jesus",
                fecha = HOY.Date
            };
            foreach (LineaPedidoVentaDTO linea in lineas)
            {
                pedido.Lineas.Add(linea);
            }
            return pedido;
        }

        // ---------------- Reglas ----------------

        [TestMethod]
        public void NombreModalidad_LosDeTNVyNesto()
        {
            Assert.AreEqual("Con cupón de productos", ReglasCursosPlantilla.NombreModalidad(ModalidadInscripcionEvento.Reembolsable));
            Assert.AreEqual("Sin cupón", ReglasCursosPlantilla.NombreModalidad(ModalidadInscripcionEvento.NoReembolsable));
        }

        [TestMethod]
        public void Modalidades_LasDosConSuImporte_YLaGratuitaNoSeVende()
        {
            List<ModalidadEventoCursoDTO> modalidades = ReglasCursosPlantilla.Modalidades(eventos.First(e => e.Id == EVENTO));

            Assert.AreEqual(2, modalidades.Count);
            Assert.AreEqual("Reembolsable", modalidades[0].Modalidad);
            Assert.AreEqual(100M, modalidades[0].Importe);
            Assert.AreEqual("NoReembolsable", modalidades[1].Modalidad);
            Assert.AreEqual("Sin cupón", modalidades[1].Nombre);
            Assert.AreEqual(60M, modalidades[1].Importe);
            Assert.AreEqual(0, ReglasCursosPlantilla.Modalidades(eventos.First(e => e.Id == 128)).Count, "gratuito: nada que vender");
        }

        [TestMethod]
        public void EsProductoDelEvento_SuProductoCursoYSuReferenciaPropiaConYSinNR()
        {
            var evento = new Evento { Id = 123, ProductoCurso = "EVENTO00123" };
            Assert.IsTrue(ReglasCursosPlantilla.EsProductoDelEvento("EVENTO00123", evento));
            Assert.IsTrue(ReglasCursosPlantilla.EsProductoDelEvento("EVENTO00123NR", evento));
            Assert.IsFalse(ReglasCursosPlantilla.EsProductoDelEvento("EVENTO00124", evento));
            Assert.IsTrue(ReglasCursosPlantilla.EsProductoDelEvento("90077 ", new Evento { Id = 5, ProductoCurso = "90077" }));
        }

        [TestMethod]
        public void QuitarLineasCurso_QuitaLasDeEventoYLasDeGrupoCUR()
        {
            var curGrupo = Producto("90078", 50);
            curGrupo.GrupoProducto = "CUR";
            PedidoVentaDTO pedido = Pedido(Producto("12345", 100), Curso(), curGrupo);

            int quitadas = ReglasCursosPlantilla.QuitarLineasCurso(pedido);

            Assert.AreEqual(2, quitadas);
            Assert.AreEqual("12345", pedido.Lineas.Single().Producto);
        }

        [TestMethod]
        public void MotivoCursoEnPedidoExistente_SoloLasLineasNuevasConEvento()
        {
            PedidoVentaDTO nuevo = Pedido(Producto("12345", 10), Curso());
            Assert.AreEqual(ReglasCursosPlantilla.MENSAJE_CURSO_EN_PEDIDO_EXISTENTE, ReglasCursosPlantilla.MotivoCursoEnPedidoExistente(nuevo));

            LineaPedidoVentaDTO yaGuardada = Curso();
            yaGuardada.id = 77;
            Assert.IsNull(ReglasCursosPlantilla.MotivoCursoEnPedidoExistente(Pedido(yaGuardada)), "el pedido del curso se puede guardar");
            Assert.IsNull(ReglasCursosPlantilla.MotivoCursoEnPedidoExistente(Pedido(Producto("12345", 10))));
        }

        // ---------------- Regresión: 100 € de productos + 150 € de curso ----------------

        [TestMethod]
        public void PortesYGanavisiones_100DeProductosMas150DeCurso_SeCalculanSobre100()
        {
            LineaPedidoVentaDTO curso = Curso(precio: 150);
            curso.GrupoProducto = "CUR";
            PedidoVentaDTO pedido = Pedido(Producto("12345", 100), curso);
            pedido.VincularLineas();

            // Portes: la base del pedido de productos no cuenta el curso aunque llegue (NestoApp#244)
            Assert.AreEqual(100M, GestorPortes.CalcularBaseImponibleProductos(pedido.Lineas));

            // Ganavisiones (y el resto de cálculos): sin las líneas de curso, la base es la de los productos
            _ = ReglasCursosPlantilla.QuitarLineasCurso(pedido);
            decimal baseGanavisiones = NestoAPI.Controllers.PedidosClienteController.BaseImponibleBonificable(pedido,
                new Dictionary<string, string> { ["12345"] = "COS", ["90077"] = "CUR" });
            Assert.AreEqual(100M, baseGanavisiones);
        }

        [TestMethod]
        public async Task Orquestador_100DeProductosMas150DeCurso_ElPedidoDeProductosVaSinElCurso()
        {
            PedidoVentaDTO pedido = Pedido(Producto("12345", 100), Curso(precio: 150));
            PreparacionCursos preparacion = await Servicio().Preparar(pedido);
            PedidoVentaDTO productosCreados = null;

            ResultadoPedidoConCursos resultado = await OrquestadorPedidoConCursos.Crear(pedido, preparacion,
                p => { productosCreados = p; p.numero = 930100; return Task.FromResult<IHttpActionResult>(Creado(p)); },
                c => Task.FromResult(new PedidoAdicionalDTO { Numero = 930101, Motivo = ReglasCursosPlantilla.MOTIVO_CURSOS }));

            Assert.AreEqual(1, productosCreados.Lineas.Count, "el curso no va en el pedido de productos (series nunca mezcladas)");
            Assert.AreEqual(100M, GestorPortes.CalcularBaseImponibleProductos(productosCreados.Lineas));
            Assert.AreEqual(930100, resultado.Respuesta.numero, "Location y cuerpo: el de productos, como hoy");
            Assert.AreEqual(1, resultado.Respuesta.PedidosAdicionales.Count);
            Assert.AreEqual(930101, resultado.Respuesta.PedidosAdicionales[0].Numero);
            Assert.AreEqual("Cursos", resultado.Respuesta.PedidosAdicionales[0].Motivo);
        }

        // ---------------- Separación ----------------

        [TestMethod]
        public async Task Orquestador_SoloCursos_NoSeSeparaYElPrincipalEsElDeCursosEnCV()
        {
            PedidoVentaDTO pedido = Pedido(Curso(modalidad: "NoReembolsable", precio: 999), Curso(EVENTO_ONLINE, precio: 1));
            PreparacionCursos preparacion = await Servicio().Preparar(pedido);
            bool productos = false;
            int llamadas = 0;

            ResultadoPedidoConCursos resultado = await OrquestadorPedidoConCursos.Crear(pedido, preparacion,
                p => { productos = true; return Task.FromResult<IHttpActionResult>(Creado(p)); },
                c => { llamadas++; return Task.FromResult(new PedidoAdicionalDTO { Numero = 930200, Motivo = "Cursos" }); });

            Assert.IsFalse(productos, "sin productos no se crea pedido NV");
            Assert.AreEqual(1, llamadas, "un solo pedido para los dos cursos");
            Assert.AreEqual(930200, resultado.Respuesta.numero);
            Assert.AreEqual("CV", resultado.Respuesta.serie);
            Assert.AreEqual("PRE", resultado.Respuesta.plazosPago);
            Assert.IsNull(resultado.Respuesta.PedidosAdicionales);
            CollectionAssert.AreEqual(new[] { 60M, 150M }, resultado.Respuesta.Lineas.Select(l => l.PrecioUnitario).ToArray(), "el precio es el de la modalidad");
        }

        [TestMethod]
        public async Task Orquestador_DosCursosYProductos_UnSoloPedidoDeCursosConLosDos_YLosProductosNoCambian()
        {
            PedidoVentaDTO pedido = Pedido(Producto("12345", 100), Curso(), Curso(EVENTO_ONLINE));
            PreparacionCursos preparacion = await Servicio().Preparar(pedido);
            var pedidosDeCursos = new List<IReadOnlyList<CursoDelPedido>>();
            PedidoVentaDTO productosCreados = null;

            ResultadoPedidoConCursos resultado = await OrquestadorPedidoConCursos.Crear(pedido, preparacion,
                p => { productosCreados = p; p.numero = 930300; return Task.FromResult<IHttpActionResult>(Creado(p)); },
                c => { pedidosDeCursos.Add(c); return Task.FromResult(new PedidoAdicionalDTO { Numero = 930301, Motivo = "Cursos" }); });

            Assert.AreEqual(1, pedidosDeCursos.Count, "UN pedido de cursos");
            CollectionAssert.AreEqual(new[] { EVENTO, EVENTO_ONLINE }, pedidosDeCursos[0].Select(c => c.Evento.Id).ToArray());
            Assert.AreEqual(930301, resultado.Respuesta.PedidosAdicionales.Single().Numero, "Nesto lee PedidosAdicionales [{Numero, Motivo}]");
            Assert.AreEqual("Cursos", resultado.Respuesta.PedidosAdicionales.Single().Motivo);
            Assert.AreEqual(930300, resultado.Respuesta.numero);
            Assert.AreEqual("12345", productosCreados.Lineas.Single().Producto, "los productos, en su pedido NV como siempre");
            Assert.AreEqual(100M, GestorPortes.CalcularBaseImponibleProductos(productosCreados.Lineas));
        }

        [TestMethod]
        public async Task Orquestador_SiNoSePuedeGenerarElEnlace_ElPedidoQuedaYSeAvisa()
        {
            PedidoVentaDTO pedido = Pedido(Producto("12345", 100), Curso());
            PreparacionCursos preparacion = await Servicio().Preparar(pedido);

            ResultadoPedidoConCursos resultado = await OrquestadorPedidoConCursos.Crear(pedido, preparacion,
                p => Task.FromResult<IHttpActionResult>(Creado(p)),
                c => Task.FromResult(new PedidoAdicionalDTO { Numero = 930310, Motivo = "Cursos", Aviso = "No se ha podido generar el enlace de pago." }));

            Assert.AreEqual(930310, resultado.Respuesta.PedidosAdicionales.Single().Numero);
            Assert.AreEqual("No se ha podido generar el enlace de pago.", resultado.Respuesta.AvisosCursos.Single());
            Assert.IsFalse(Newtonsoft.Json.JsonConvert.SerializeObject(resultado.Respuesta.PedidosAdicionales).Contains("Aviso"), "el JSON de Nesto no cambia");
        }

        [TestMethod]
        public async Task Orquestador_SiElPedidoDeProductosNoSeCrea_NoSeCreaNingunCurso()
        {
            PedidoVentaDTO pedido = Pedido(Producto("12345", 100), Curso());
            PreparacionCursos preparacion = await Servicio().Preparar(pedido);
            bool curso = false;

            ResultadoPedidoConCursos resultado = await OrquestadorPedidoConCursos.Crear(pedido, preparacion,
                p => Task.FromResult<IHttpActionResult>(new BadRequestErrorMessageResult("no", new FakeController())),
                c => { curso = true; return Task.FromResult(new PedidoAdicionalDTO()); });

            Assert.IsNotNull(resultado.NoCreado);
            Assert.IsFalse(curso);
        }

        [TestMethod]
        public async Task Orquestador_FallaElCursoTrasCrearLosProductos_SeAvisaYNoSeDeshace()
        {
            PedidoVentaDTO pedido = Pedido(Producto("12345", 100), Curso());
            PreparacionCursos preparacion = await Servicio().Preparar(pedido);

            ResultadoPedidoConCursos resultado = await OrquestadorPedidoConCursos.Crear(pedido, preparacion,
                p => Task.FromResult<IHttpActionResult>(Creado(p)),
                c => throw new NestoBusinessException("No quedan plazas."));

            Assert.IsNull(resultado.Respuesta.PedidosAdicionales);
            StringAssert.Contains(resultado.Respuesta.AvisosCursos.Single(), "Curso de cejas");
        }

        [TestMethod]
        public async Task Orquestador_SoloCursosYFallaElPrimero_Lanza()
        {
            PedidoVentaDTO pedido = Pedido(Curso());
            PreparacionCursos preparacion = await Servicio().Preparar(pedido);

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => OrquestadorPedidoConCursos.Crear(pedido, preparacion,
                p => Task.FromResult<IHttpActionResult>(Creado(p)),
                c => throw new NestoBusinessException("No quedan plazas.")));
        }

        // ---------------- Preparar: comprobaciones y precio por modalidad ----------------

        [TestMethod]
        public async Task Preparar_PrecioPorModalidad_SinCupon60ConCupon100()
        {
            PreparacionCursos sinCupon = await Servicio().Preparar(Pedido(Curso(modalidad: "NoReembolsable", precio: 1)));
            PreparacionCursos conCupon = await Servicio().Preparar(Pedido(Curso(modalidad: "Reembolsable", precio: 1)));

            Assert.IsNull(sinCupon.Error);
            Assert.AreEqual(60M, sinCupon.Cursos.Single().Importe);
            Assert.AreEqual(ModalidadInscripcionEvento.NoReembolsable, sinCupon.Cursos.Single().Modalidad);
            Assert.AreEqual(100M, conCupon.Cursos.Single().Importe);
            Assert.AreEqual("90077", conCupon.Cursos.Single().ProductoFacturar);
        }

        [TestMethod]
        public async Task Preparar_ReferenciaPropiaNR_SeFacturaConLaSinNRSiExiste()
        {
            eventos.First(e => e.Id == EVENTO).ProductoCurso = "EVENTO00123NR";
            productos.Add(new Producto { Empresa = "1", Número = "EVENTO00123", Grupo = "CUR", Ficticio = true });
            productos.Add(new Producto { Empresa = "1", Número = "EVENTO00123NR", Grupo = "CUR", Ficticio = true });
            LineaPedidoVentaDTO curso = Curso(modalidad: "NoReembolsable");
            curso.Producto = "EVENTO00123NR";

            PreparacionCursos preparacion = await Servicio().Preparar(Pedido(curso));

            Assert.IsNull(preparacion.Error);
            Assert.AreEqual("EVENTO00123", preparacion.Cursos.Single().ProductoFacturar);
        }

        [TestMethod]
        public async Task Preparar_CURSinEventoConProductos_400DeUsted()
        {
            var cur = Producto("90077", 100);
            cur.GrupoProducto = null;

            PreparacionCursos preparacion = await Servicio().Preparar(Pedido(Producto("12345", 10), cur));

            StringAssert.Contains(preparacion.Error, "elija el evento y la modalidad");
            StringAssert.Contains(preparacion.Error, "por favor");
        }

        [TestMethod]
        public async Task Preparar_CURSinEventoSoloCursos_ComoHastaAhora()
        {
            // Las tiendas, #612 y los pedidos de solo cursos: SerieSegunLineas los pasa a CV, no se toca
            PreparacionCursos preparacion = await Servicio().Preparar(Pedido(Producto("90077", 100)));

            Assert.IsNull(preparacion.Error);
            Assert.IsFalse(preparacion.HayCursos);
        }

        [TestMethod]
        public async Task Preparar_CanalExterno_NoSeMiran()
        {
            LineaPedidoVentaDTO cur = Producto("90077", 100);
            LineaPedidoVentaDTO otro = Producto("12345", 10);
            cur.formaVenta = otro.formaVenta = Constantes.FormasVenta.AMAZON;

            PreparacionCursos preparacion = await Servicio().Preparar(Pedido(otro, cur));

            Assert.IsNull(preparacion.Error);
        }

        [TestMethod]
        public async Task Preparar_SinModalidad_400()
        {
            PreparacionCursos preparacion = await Servicio().Preparar(Pedido(Curso(modalidad: null)));
            StringAssert.Contains(preparacion.Error, "modalidad");
        }

        [TestMethod]
        public async Task Preparar_EventoLleno_400()
        {
            PreparacionCursos preparacion = await Servicio().Preparar(Pedido(Curso(EVENTO_LLENO)));
            StringAssert.Contains(preparacion.Error, "No quedan plazas");
        }

        [TestMethod]
        public async Task Preparar_YaInscrito_400()
        {
            inscripciones.Add(new EventoInscripcion { Id = 2, EventoId = EVENTO, Empresa = "1", Cliente = "15191", Contacto = "0  ", Estado = "Reservada" });
            PreparacionCursos preparacion = await Servicio().Preparar(Pedido(Curso()));
            StringAssert.Contains(preparacion.Error, "ya está inscrito");
        }

        [TestMethod]
        public async Task Preparar_Gratuita_Presupuesto_YCantidad_400()
        {
            Assert.IsNotNull((await Servicio().Preparar(Pedido(Curso(modalidad: "Gratuita")))).Error);

            PedidoVentaDTO presupuesto = Pedido(Curso());
            presupuesto.EsPresupuesto = true;
            Assert.AreEqual(ReglasCursosPlantilla.MENSAJE_CURSO_EN_PRESUPUESTO, (await Servicio().Preparar(presupuesto)).Error);

            LineaPedidoVentaDTO dos = Curso();
            dos.Cantidad = 2;
            StringAssert.Contains((await Servicio().Preparar(Pedido(dos))).Error, "1 unidad");
        }

        [TestMethod]
        public async Task Preparar_EventoPasadoOInactivoOProductoAjeno_400()
        {
            Assert.IsNotNull((await Servicio().Preparar(Pedido(Curso(126)))).Error);
            Assert.IsNotNull((await Servicio().Preparar(Pedido(Curso(127)))).Error);
            LineaPedidoVentaDTO ajeno = Curso();
            ajeno.Producto = "12345";
            StringAssert.Contains((await Servicio().Preparar(Pedido(ajeno))).Error, "no es el curso");
        }

        // ---------------- El pedido del curso: inscripción ligada, cabecera y línea ----------------

        [TestMethod]
        public async Task CrearPedidoCursos_DosCursos_DosInscripcionesUnPedidoConLasDosLigadasYUnEnlacePorElTotal()
        {
            IServicioInscripcionesEventos servicioInscripciones = A.Fake<IServicioInscripcionesEventos>();
            A.CallTo(() => servicioInscripciones.Inscribir(EVENTO, A<InscribirEventoDTO>._, A<IPrincipal>._))
                .Returns(Task.FromResult(new InscripcionEventoDTO { Id = 501 }));
            A.CallTo(() => servicioInscripciones.Inscribir(EVENTO_ONLINE, A<InscribirEventoDTO>._, A<IPrincipal>._))
                .Returns(Task.FromResult(new InscripcionEventoDTO { Id = 502 }));
            A.CallTo(() => servicioInscripciones.GenerarEnlacePagoPedidoCursos(930400, A<List<int>>._, A<IPrincipal>._))
                .Returns(Task.FromResult(new InscripcionEventoDTO { Id = 501, UrlPago = "https://api.nuevavision.es/pago/x" }));
            ICreadorPedidoCursoPlantilla creador = A.Fake<ICreadorPedidoCursoPlantilla>();
            List<CursoDelPedido> creados = null;
            A.CallTo(() => creador.Crear(A<PedidoVentaDTO>._, A<Cliente>._, A<IReadOnlyList<CursoDelPedido>>._))
                .ReturnsLazily((PedidoVentaDTO p, Cliente f, IReadOnlyList<CursoDelPedido> c) => { creados = c.ToList(); return Task.FromResult(930400); });
            IAvisadorCursoPlantilla avisador = A.Fake<IAvisadorCursoPlantilla>();
            PedidoVentaDTO pedido = Pedido(Curso(modalidad: "NoReembolsable"), Curso(EVENTO_ONLINE));
            ServicioCursosPlantilla servicio = Servicio(servicioInscripciones, creador, avisador);
            PreparacionCursos preparacion = await servicio.Preparar(pedido);

            PedidoAdicionalDTO creado = await servicio.CrearPedidoCursos(pedido, preparacion.Ficha, preparacion.Cursos, null);

            Assert.AreEqual(930400, creado.Numero);
            Assert.AreEqual("Cursos", creado.Motivo);
            Assert.IsNull(creado.Aviso);
            A.CallTo(() => servicioInscripciones.Inscribir(A<int>._, A<InscribirEventoDTO>.That.Matches(i =>
                !i.GenerarEnlacePago && i.Correo == null && i.Origen == "Nesto" && i.Cliente == "15191"),
                A<IPrincipal>._)).MustHaveHappenedTwiceExactly();
            A.CallTo(() => servicioInscripciones.Inscribir(EVENTO, A<InscribirEventoDTO>.That.Matches(i => i.Modalidad == "NoReembolsable"), A<IPrincipal>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => creador.Crear(pedido, A<Cliente>._, A<IReadOnlyList<CursoDelPedido>>._)).MustHaveHappenedOnceExactly();
            CollectionAssert.AreEqual(new int?[] { 501, 502 }, creados.Select(c => c.InscripcionId).ToArray(), "cada línea, ligada a su inscripción");
            A.CallTo(() => servicioInscripciones.GenerarEnlacePagoPedidoCursos(930400, A<List<int>>.That.Matches(l => l.SequenceEqual(new[] { 501, 502 })), A<IPrincipal>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => avisador.Avisar(pedido, A<IReadOnlyList<CursoDelPedido>>.That.Matches(c => c.Count == 2), A<IReadOnlyList<InscripcionEventoDTO>>._,
                "https://api.nuevavision.es/pago/x", 930400, FormaPagoInscripcionEvento.Tarjeta)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task CrearPedidoCursos_SiFallaElPedido_SeAnulanLasInscripciones()
        {
            IServicioInscripcionesEventos servicioInscripciones = A.Fake<IServicioInscripcionesEventos>();
            int id = 600;
            A.CallTo(() => servicioInscripciones.Inscribir(A<int>._, A<InscribirEventoDTO>._, A<IPrincipal>._))
                .ReturnsLazily(() => Task.FromResult(new InscripcionEventoDTO { Id = ++id }));
            ICreadorPedidoCursoPlantilla creador = A.Fake<ICreadorPedidoCursoPlantilla>();
            A.CallTo(() => creador.Crear(A<PedidoVentaDTO>._, A<Cliente>._, A<IReadOnlyList<CursoDelPedido>>._))
                .Throws(new InvalidOperationException("CHECK"));
            ServicioCursosPlantilla servicio = Servicio(servicioInscripciones, creador);
            PedidoVentaDTO pedido = Pedido(Curso(), Curso(EVENTO_ONLINE));
            PreparacionCursos preparacion = await servicio.Preparar(pedido);

            _ = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.CrearPedidoCursos(pedido, preparacion.Ficha, preparacion.Cursos, null));

            A.CallTo(() => servicioInscripciones.Anular(601, A<IPrincipal>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => servicioInscripciones.Anular(602, A<IPrincipal>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => servicioInscripciones.GenerarEnlacePagoPedidoCursos(A<int>._, A<List<int>>._, A<IPrincipal>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CrearPedidoCursos_SiFallaElEnlace_ElPedidoQuedaConAvisoYElCorreoSinEnlace()
        {
            IServicioInscripcionesEventos servicioInscripciones = A.Fake<IServicioInscripcionesEventos>();
            A.CallTo(() => servicioInscripciones.Inscribir(A<int>._, A<InscribirEventoDTO>._, A<IPrincipal>._))
                .Returns(Task.FromResult(new InscripcionEventoDTO { Id = 701 }));
            A.CallTo(() => servicioInscripciones.GenerarEnlacePagoPedidoCursos(A<int>._, A<List<int>>._, A<IPrincipal>._))
                .Throws(new InvalidOperationException("Redsys caído"));
            ICreadorPedidoCursoPlantilla creador = A.Fake<ICreadorPedidoCursoPlantilla>();
            A.CallTo(() => creador.Crear(A<PedidoVentaDTO>._, A<Cliente>._, A<IReadOnlyList<CursoDelPedido>>._)).Returns(Task.FromResult(930410));
            IAvisadorCursoPlantilla avisador = A.Fake<IAvisadorCursoPlantilla>();
            ServicioCursosPlantilla servicio = Servicio(servicioInscripciones, creador, avisador);
            PedidoVentaDTO pedido = Pedido(Curso());
            PreparacionCursos preparacion = await servicio.Preparar(pedido);

            PedidoAdicionalDTO creado = await servicio.CrearPedidoCursos(pedido, preparacion.Ficha, preparacion.Cursos, null);

            Assert.AreEqual(930410, creado.Numero);
            StringAssert.Contains(creado.Aviso, "enlace de pago");
            StringAssert.Contains(creado.Aviso, "por favor");
            A.CallTo(() => servicioInscripciones.Anular(A<int>._, A<IPrincipal>._)).MustNotHaveHappened();
            A.CallTo(() => avisador.Avisar(pedido, A<IReadOnlyList<CursoDelPedido>>._, A<IReadOnlyList<InscripcionEventoDTO>>._, null, 930410, FormaPagoInscripcionEvento.Tarjeta))
                .MustHaveHappenedOnceExactly();
        }

        // ---------------- Sugerencia 899: la forma de pago del pedido y el correo que se pregunta en Nesto ----------------

        private (IServicioInscripcionesEventos inscripciones, IAvisadorCursoPlantilla avisador, ServicioCursosPlantilla servicio) ServicioParaCrear(int numero)
        {
            IServicioInscripcionesEventos servicioInscripciones = A.Fake<IServicioInscripcionesEventos>();
            A.CallTo(() => servicioInscripciones.Inscribir(A<int>._, A<InscribirEventoDTO>._, A<IPrincipal>._))
                .Returns(Task.FromResult(new InscripcionEventoDTO { Id = 14 }));
            A.CallTo(() => servicioInscripciones.GenerarEnlacePagoPedidoCursos(A<int>._, A<List<int>>._, A<IPrincipal>._))
                .Returns(Task.FromResult(new InscripcionEventoDTO { Id = 14, UrlPago = "https://api.nuevavision.es/pago/x" }));
            ICreadorPedidoCursoPlantilla creador = A.Fake<ICreadorPedidoCursoPlantilla>();
            A.CallTo(() => creador.Crear(A<PedidoVentaDTO>._, A<Cliente>._, A<IReadOnlyList<CursoDelPedido>>._)).Returns(Task.FromResult(numero));
            IAvisadorCursoPlantilla avisador = A.Fake<IAvisadorCursoPlantilla>();
            return (servicioInscripciones, avisador, Servicio(servicioInscripciones, creador, avisador));
        }

        [TestMethod]
        public async Task CrearPedidoCursos_PorTransferencia_InscripcionTransferenciaSinEnlaceDeTarjetaYCorreoDeTransferencia()
        {
            // Regresión sugerencia 899 (pedido 929655, inscripción 14): por transferencia se generaba el enlace de tarjeta y la
            // inscripción quedaba con FormaPago «Tarjeta».
            var (servicioInscripciones, avisador, servicio) = ServicioParaCrear(929655);
            PedidoVentaDTO pedido = Pedido(Curso());
            pedido.formaPago = "TRN";
            PreparacionCursos preparacion = await servicio.Preparar(pedido);

            PedidoAdicionalDTO creado = await servicio.CrearPedidoCursos(pedido, preparacion.Ficha, preparacion.Cursos, null);

            Assert.IsNull(creado.Aviso);
            A.CallTo(() => servicioInscripciones.Inscribir(A<int>._, A<InscribirEventoDTO>.That.Matches(i => i.FormaPago == "Transferencia" && !i.GenerarEnlacePago),
                A<IPrincipal>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => servicioInscripciones.GenerarEnlacePagoPedidoCursos(A<int>._, A<List<int>>._, A<IPrincipal>._)).MustNotHaveHappened();
            A.CallTo(() => avisador.Avisar(pedido, A<IReadOnlyList<CursoDelPedido>>._, A<IReadOnlyList<InscripcionEventoDTO>>._, null, 929655,
                FormaPagoInscripcionEvento.Transferencia)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task CrearPedidoCursos_EnEfectivo_InscripcionEfectivoSinEnlace()
        {
            var (servicioInscripciones, avisador, servicio) = ServicioParaCrear(929656);
            PedidoVentaDTO pedido = Pedido(Curso());
            pedido.formaPago = "EFC";
            PreparacionCursos preparacion = await servicio.Preparar(pedido);

            _ = await servicio.CrearPedidoCursos(pedido, preparacion.Ficha, preparacion.Cursos, null);

            A.CallTo(() => servicioInscripciones.Inscribir(A<int>._, A<InscribirEventoDTO>.That.Matches(i => i.FormaPago == "Efectivo"), A<IPrincipal>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => servicioInscripciones.GenerarEnlacePagoPedidoCursos(A<int>._, A<List<int>>._, A<IPrincipal>._)).MustNotHaveHappened();
            A.CallTo(() => avisador.Avisar(pedido, A<IReadOnlyList<CursoDelPedido>>._, A<IReadOnlyList<InscripcionEventoDTO>>._, null, 929656,
                FormaPagoInscripcionEvento.Efectivo)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task CrearPedidoCursos_ConTarjeta_SinFormaPagoEnLaInscripcionYConEnlace_ComoHastaAhora()
        {
            var (servicioInscripciones, avisador, servicio) = ServicioParaCrear(929657);
            PedidoVentaDTO pedido = Pedido(Curso());
            pedido.formaPago = "TAR";
            PreparacionCursos preparacion = await servicio.Preparar(pedido);

            _ = await servicio.CrearPedidoCursos(pedido, preparacion.Ficha, preparacion.Cursos, null);

            A.CallTo(() => servicioInscripciones.Inscribir(A<int>._, A<InscribirEventoDTO>.That.Matches(i => i.FormaPago == null), A<IPrincipal>._))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => servicioInscripciones.GenerarEnlacePagoPedidoCursos(929657, A<List<int>>._, A<IPrincipal>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => avisador.Avisar(pedido, A<IReadOnlyList<CursoDelPedido>>._, A<IReadOnlyList<InscripcionEventoDTO>>._,
                "https://api.nuevavision.es/pago/x", 929657, FormaPagoInscripcionEvento.Tarjeta)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task CrearPedidoCursos_SiElUsuarioDiceQueNo_NoSeMandaCorreo()
        {
            var (servicioInscripciones, avisador, servicio) = ServicioParaCrear(929658);
            PedidoVentaDTO pedido = Pedido(Curso());
            pedido.formaPago = "TAR";
            pedido.EnviarCorreoCursos = false;
            PreparacionCursos preparacion = await servicio.Preparar(pedido);

            _ = await servicio.CrearPedidoCursos(pedido, preparacion.Ficha, preparacion.Cursos, null);

            A.CallTo(() => servicioInscripciones.GenerarEnlacePagoPedidoCursos(929658, A<List<int>>._, A<IPrincipal>._))
                .MustHaveHappenedOnceExactly(); // el enlace se genera igual (se puede mandar desde el pedido)
            A.CallTo(() => avisador.Avisar(A<PedidoVentaDTO>._, A<IReadOnlyList<CursoDelPedido>>._, A<IReadOnlyList<InscripcionEventoDTO>>._,
                A<string>._, A<int>._, A<FormaPagoInscripcionEvento>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CrearPedidoCursos_SinRespuestaDelUsuario_SeMandaElCorreo_ComoHastaAhora()
        {
            // NestoApp y TNV no mandan EnviarCorreoCursos: se manda, como hasta ahora
            var (_, avisador, servicio) = ServicioParaCrear(929659);
            PedidoVentaDTO pedido = Pedido(Curso());
            pedido.formaPago = "TRN";
            pedido.EnviarCorreoCursos = null;
            PreparacionCursos preparacion = await servicio.Preparar(pedido);

            _ = await servicio.CrearPedidoCursos(pedido, preparacion.Ficha, preparacion.Cursos, null);

            A.CallTo(() => avisador.Avisar(pedido, A<IReadOnlyList<CursoDelPedido>>._, A<IReadOnlyList<InscripcionEventoDTO>>._, null, 929659,
                FormaPagoInscripcionEvento.Transferencia)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void FormaPagoInscripcion_TrnTransferencia_EfcEfectivo_ElRestoTarjeta()
        {
            Assert.AreEqual(FormaPagoInscripcionEvento.Transferencia, ReglasCursosPlantilla.FormaPagoInscripcion("TRN"));
            Assert.AreEqual(FormaPagoInscripcionEvento.Transferencia, ReglasCursosPlantilla.FormaPagoInscripcion(" trn "));
            Assert.AreEqual(FormaPagoInscripcionEvento.Efectivo, ReglasCursosPlantilla.FormaPagoInscripcion("EFC"));
            Assert.AreEqual(FormaPagoInscripcionEvento.Tarjeta, ReglasCursosPlantilla.FormaPagoInscripcion("TAR"));
            Assert.AreEqual(FormaPagoInscripcionEvento.Tarjeta, ReglasCursosPlantilla.FormaPagoInscripcion(null));
            Assert.AreEqual(FormaPagoInscripcionEvento.Tarjeta, ReglasCursosPlantilla.FormaPagoInscripcion(""));
        }

        [TestMethod]
        public void Correo_PorTransferencia_ConIbanConceptoEImporte_SinBotonDeTarjeta()
        {
            var curso = new CursoDelPedido { Evento = eventos.First(e => e.Id == EVENTO), Modalidad = ModalidadInscripcionEvento.Reembolsable, Importe = 120 };
            var datos = new DatosTransferenciaPedidoDTO
            {
                Iban = "ES12 3456 7890 1234 5678 9012",
                Titular = "NUEVA VISIÓN, S.A.",
                Concepto = "Cliente 35100 - Pedido 929655",
                Importe = 120
            };

            using (MailMessage correo = AvisadorCursoPlantilla.Correo(new[] { curso }, null, new DateTime(2026, 11, 3), 929655, "MARÍA", "maria@example.com",
                FormaPagoInscripcionEvento.Transferencia, datos))
            {
                StringAssert.Contains(correo.Subject, "datos para la transferencia");
                StringAssert.Contains(correo.Body, "haga una transferencia");
                StringAssert.Contains(correo.Body, "ES12 3456 7890 1234 5678 9012");
                StringAssert.Contains(correo.Body, System.Net.WebUtility.HtmlEncode("NUEVA VISIÓN, S.A."));
                StringAssert.Contains(correo.Body, "Cliente 35100 - Pedido 929655");
                StringAssert.Contains(correo.Body, "120,00 €");
                Assert.IsFalse(correo.Body.Contains("Pagar mi plaza"), "sin botón de tarjeta");
                Assert.IsFalse(correo.Body.Contains("enlace de pago"), "sin enlace de pago");
            }
        }

        [TestMethod]
        public void Correo_PorTransferenciaSinIban_PidaLosDatos()
        {
            var curso = new CursoDelPedido { Evento = eventos.First(e => e.Id == EVENTO), Modalidad = ModalidadInscripcionEvento.Reembolsable, Importe = 120 };

            using (MailMessage correo = AvisadorCursoPlantilla.Correo(new[] { curso }, null, null, 929655, null, "maria@example.com",
                FormaPagoInscripcionEvento.Transferencia, new DatosTransferenciaPedidoDTO { Concepto = "Cliente 35100 - Pedido 929655", Importe = 120 }))
            {
                StringAssert.Contains(correo.Body, "pídanoslos");
                StringAssert.Contains(correo.Body, "Cliente 35100 - Pedido 929655");
            }
        }

        [TestMethod]
        public void Correo_EnEfectivo_PagueEnLaTienda_SinEnlace()
        {
            var curso = new CursoDelPedido { Evento = eventos.First(e => e.Id == EVENTO), Modalidad = ModalidadInscripcionEvento.Reembolsable, Importe = 120 };

            using (MailMessage correo = AvisadorCursoPlantilla.Correo(new[] { curso }, null, null, 929656, null, "maria@example.com", FormaPagoInscripcionEvento.Efectivo))
            {
                StringAssert.Contains(correo.Subject, "reserva");
                StringAssert.Contains(correo.Body, "páguela en nuestra tienda");
                Assert.IsFalse(correo.Body.Contains("enlace de pago"));
                Assert.IsFalse(correo.Body.Contains("transferencia"));
            }
        }

        [TestMethod]
        public void DatosTransferencia_ConceptoDelPedidoDeCursosIbanDeLaEmpresaEImporteDeLosCursos()
        {
            global::NestoAPI.Infraestructure.Cobros.ILectorDatosPagoEmpresa lector = A.Fake<global::NestoAPI.Infraestructure.Cobros.ILectorDatosPagoEmpresa>();
            A.CallTo(() => lector.Leer("1")).Returns(new global::NestoAPI.Models.Cobros.DatosPagoAviso { Iban = " ES12 3456 ", Titular = "NUEVA VISIÓN " });
            var avisador = new AvisadorCursoPlantilla(db, A.Fake<IServicioCorreoElectronico>(), lector);
            var cursos = new[]
            {
                new CursoDelPedido { Evento = eventos.First(e => e.Id == EVENTO), Importe = 60 },
                new CursoDelPedido { Evento = eventos.First(e => e.Id == EVENTO_ONLINE), Importe = 150 }
            };

            DatosTransferenciaPedidoDTO datos = avisador.DatosTransferencia("1", "35100", 929655, cursos);

            Assert.AreEqual("ES12 3456", datos.Iban);
            Assert.AreEqual("NUEVA VISIÓN", datos.Titular);
            Assert.AreEqual("Cliente 35100 - Pedido 929655", datos.Concepto);
            Assert.AreEqual(210M, datos.Importe);
        }

        [TestMethod]
        public void AvisosSinEnlace_SegunLaFormaDePago()
        {
            StringAssert.Contains(ReglasInscripcionEvento.ComoPagarSinEnlace("Transferencia"), "por transferencia");
            Assert.IsFalse(ReglasInscripcionEvento.ComoPagarSinEnlace("Transferencia").Contains("enlace"));
            Assert.AreEqual("Puede pagarla en su tienda.", ReglasInscripcionEvento.ComoPagarSinEnlace("Efectivo"));
            Assert.AreEqual("Puede pagarla en su tienda o pedirnos el enlace de pago.", ReglasInscripcionEvento.ComoPagarSinEnlace("Tarjeta"));
            Assert.AreEqual("Puede pagarla en su tienda o pedirnos el enlace de pago.", ReglasInscripcionEvento.ComoPagarSinEnlace(null));
        }

        [TestMethod]
        public void AvisoPlazoDePago_InscripcionPorTransferenciaSinEnlace_DiceTransferenciaNoEnlace()
        {
            var aviso = new AvisoPagoInscripcion
            {
                Evento = eventos.First(e => e.Id == EVENTO),
                Inscripcion = new EventoInscripcion { Id = 14, Empresa = "1", Cliente = "35100", Contacto = "0", FormaPago = "Transferencia" },
                FechaLimite = new DateTime(2026, 11, 3),
                FechaLiberacion = new DateTime(2026, 11, 4)
            };

            using (MailMessage correo = RecordatorioEventosJobsService.CorreoPago(aviso, "MARÍA", "maria@example.com"))
            {
                StringAssert.Contains(correo.Body, "por transferencia");
                Assert.IsFalse(correo.Body.Contains("Pagar mi plaza"));
                Assert.IsFalse(correo.Body.Contains("enlace de pago"));
            }
        }

        [TestMethod]
        public void AvisoEventoLleno746_InscripcionEnEfectivoSinEnlace_DiceTiendaNoEnlace()
        {
            var aviso = new AvisoPlazasLlenasInscripcion
            {
                Evento = eventos.First(e => e.Id == EVENTO),
                Inscripcion = new EventoInscripcion { Id = 15, Empresa = "1", Cliente = "35100", Contacto = "0", FormaPago = "Efectivo" },
                Plazo = new DateTime(2026, 11, 3, 14, 0, 0)
            };

            using (MailMessage correo = PlazasLlenasEventosJobsService.CorreoAviso(aviso, null, "maria@example.com"))
            {
                StringAssert.Contains(correo.Body, "Puede pagarla en su tienda.");
                Assert.IsFalse(correo.Body.Contains("enlace de pago"));
            }
        }

        [TestMethod]
        public async Task CabeceraYLinea_PrepagoTiendaDelEventoSinDescuentosYSerieCV()
        {
            PedidoVentaDTO pedido = Pedido(Curso(EVENTO_ONLINE, "Reembolsable"), Curso());
            pedido.formaPago = "TRN";
            PreparacionCursos preparacion = await Servicio().Preparar(pedido);
            preparacion.Cursos[0].InscripcionId = 503;
            preparacion.Cursos[1].InscripcionId = 504;
            CursoDelPedido curso = preparacion.Cursos[0];

            CabPedidoVta cabecera = ReglasCursosPlantilla.Cabecera(pedido, preparacion.Ficha, 930500, preparacion.Cursos, HOY.Date);
            LineaPedidoVentaDTO linea = ReglasCursosPlantilla.Linea(pedido, curso, HOY.Date);
            LineaPedidoVentaDTO otra = ReglasCursosPlantilla.Linea(pedido, preparacion.Cursos[1], HOY.Date);

            Assert.AreEqual("PRE", cabecera.PlazosPago);
            Assert.AreEqual("TRN", cabecera.Forma_Pago);
            Assert.AreEqual(Constantes.Pedidos.PERIODO_FACTURACION_NORMAL, cabecera.Periodo_Facturacion);
            Assert.AreEqual("JE", cabecera.Vendedor, "sin vendedor en el pedido, el de la ficha");
            StringAssert.Contains(cabecera.Comentarios, "inscripción 503");
            StringAssert.Contains(cabecera.Comentarios, "inscripción 504");
            Assert.AreEqual("ALC", linea.almacen, "el del evento, también en un online; nunca ALG");
            Assert.AreEqual("ALC", linea.delegacion);
            Assert.AreEqual("REI", otra.almacen, "cada línea, la tienda de su evento");
            Assert.AreEqual(150M, linea.PrecioUnitario);
            Assert.AreEqual(0M, linea.DescuentoLinea);
            Assert.IsFalse(linea.AplicarDescuento);
            Assert.AreEqual(1, linea.Cantidad);
            Assert.AreEqual(Constantes.EstadosLineaVenta.PENDIENTE, linea.estado, "hasta que se pague su inscripción no entra en ningún albarán");
            Assert.AreEqual("CV", SerieSegunLineas.Resolver(cabecera.Serie, "1", new[] { new LinPedidoVta { Grupo = "CUR" }, new LinPedidoVta { Grupo = "CUR" } }));
        }

        [TestMethod]
        public void GestorPrecios_NoValidaLaLineaDeCurso_YLaDejaEnSuSitio()
        {
            PedidoVentaDTO pedido = Pedido(Curso(precio: 60));
            IServicioPrecios precios = A.Fake<IServicioPrecios>();

            RespuestaValidacion respuesta = GestorPrecios.EsPedidoValido(pedido, precios);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            Assert.AreEqual(1, pedido.Lineas.Count);
            Assert.AreEqual(60M, pedido.Lineas.Single().PrecioUnitario);
        }

        // ---------------- Catálogo: PorProducto y búsqueda ----------------

        [TestMethod]
        public async Task EventosDeProducto_FuturosActivosConPlazas_ElMasCercanoAntes()
        {
            List<EventoCursoDTO> lista = await new CatalogoCursosPlantilla(db, () => HOY).EventosDeProducto("1", "90077");

            CollectionAssert.AreEqual(new[] { EVENTO_ONLINE, EVENTO }, lista.Select(e => e.Id).ToArray(),
                "fuera el lleno, el pasado, el inactivo y el gratuito");
            EventoCursoDTO cejas = lista.Last();
            Assert.AreEqual("10:00", cejas.HoraInicio);
            Assert.AreEqual("REI", cejas.Almacen);
            Assert.AreEqual("Presencial", cejas.Formato);
            Assert.AreEqual(10, cejas.PlazasLibres);
            Assert.AreEqual("Online", lista.First().Formato);
            Assert.AreEqual(ReglasCursosPlantilla.PLAZAS_SIN_LIMITE, lista.First().PlazasLibres);
            Assert.AreEqual("Con cupón de productos", cejas.Modalidades[0].Nombre);
        }

        [TestMethod]
        public async Task LineasCursos_LaBusquedaEncuentraElCursoVendibleMarcadoEsCurso()
        {
            var catalogo = new CatalogoCursosPlantilla(db, () => HOY);

            List<LineaPlantillaVenta> porNombre = await catalogo.LineasCursos("1", "formación");
            List<LineaPlantillaVenta> otro = await catalogo.LineasCursos("1", "crema");

            Assert.AreEqual("90077", porNombre.Single().producto);
            Assert.IsTrue(porNombre.Single().EsCurso);
            Assert.AreEqual(0, otro.Count, "el resto de ficticios y productos no salen por aquí");
        }

        [TestMethod]
        public async Task LineasCursos_SinEventosVendibles_NoSaleNada()
        {
            eventos.RemoveAll(e => e.Id == EVENTO || e.Id == EVENTO_ONLINE);
            Assert.AreEqual(0, (await new CatalogoCursosPlantilla(db, () => HOY).LineasCursos("1", "90077")).Count);
        }

        // ---------------- El correo a la clienta ----------------

        [TestMethod]
        public void Correo_DeUstedConEventoFechaTiendaModalidadImporteYEnlace()
        {
            var curso = new CursoDelPedido { Evento = eventos.First(e => e.Id == EVENTO), Modalidad = ModalidadInscripcionEvento.NoReembolsable, Importe = 60 };
            const string url = "https://api.nuevavision.es/pago/abc";

            using (MailMessage correo = AvisadorCursoPlantilla.Correo(new[] { curso }, url, new DateTime(2026, 11, 3), 930600, "LUNA", "luna@example.com"))
            {
                StringAssert.Contains(correo.Subject, "Curso de cejas");
                StringAssert.Contains(correo.Body, "Le hemos reservado una plaza");
                StringAssert.Contains(correo.Body, "tienda de Reina");
                StringAssert.Contains(correo.Body, "10:00");
                StringAssert.Contains(correo.Body, System.Net.WebUtility.HtmlEncode("Sin cupón"));
                StringAssert.Contains(correo.Body, "60,00 €");
                StringAssert.Contains(correo.Body, url);
                StringAssert.Contains(correo.Body, "930600");
            }
            curso.Evento = eventos.First(e => e.Id == EVENTO_ONLINE);
            using (MailMessage online = AvisadorCursoPlantilla.Correo(new[] { curso }, url, null, 1, null, "luna@example.com"))
            {
                StringAssert.Contains(online.Body, "online");
            }
        }

        [TestMethod]
        public void Correo_DosCursos_TodosConSuImporte_ElTotalYUnSoloEnlace()
        {
            var cursos = new[]
            {
                new CursoDelPedido { Evento = eventos.First(e => e.Id == EVENTO), Modalidad = ModalidadInscripcionEvento.NoReembolsable, Importe = 60 },
                new CursoDelPedido { Evento = eventos.First(e => e.Id == EVENTO_ONLINE), Modalidad = ModalidadInscripcionEvento.Reembolsable, Importe = 150 }
            };
            const string url = "https://api.nuevavision.es/pago/total";

            using (MailMessage correo = AvisadorCursoPlantilla.Correo(cursos, url, new DateTime(2026, 10, 29), 930610, "LUNA", "luna@example.com"))
            {
                StringAssert.Contains(correo.Subject, "Curso de cejas");
                StringAssert.Contains(correo.Subject, "Webinar");
                StringAssert.Contains(correo.Body, "60,00 €");
                StringAssert.Contains(correo.Body, "150,00 €");
                StringAssert.Contains(correo.Body, "Total: 210,00 €");
                StringAssert.Contains(correo.Body, "páguelas");
                Assert.AreEqual(1, correo.Body.Split(new[] { url }, StringSplitOptions.None).Length - 1, "un solo enlace por el total");
                StringAssert.Contains(correo.Body, "930610");
            }
        }

        // ---------------- Ayudantes ----------------

        private static CreatedAtRouteNegotiatedContentResult<PedidoVentaDTO> Creado(PedidoVentaDTO pedido) =>
            new CreatedAtRouteNegotiatedContentResult<PedidoVentaDTO>("DefaultApi", new Dictionary<string, object> { ["id"] = pedido.numero }, pedido, new FakeController());

        private class FakeController : ApiController
        {
        }

        private static DbSet<T> Crear<T>(List<T> datos) where T : class
        {
            DbSet<T> fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.ToList().GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.ToList().GetEnumerator());
            return fake;
        }
    }
}
