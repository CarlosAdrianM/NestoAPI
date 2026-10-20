using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>Las lecturas de la salida en memoria: lo que se inserta se suma a lo que se lee después.</summary>
    public class TransaccionCorreccionFalsa : ITransaccionCorreccion
    {
        public bool Terminada { get; set; }
        public List<LecturaPickingAlmacen> Lecturas { get; set; } = new List<LecturaPickingAlmacen>();
        public List<(EscaneoAlmacenDTO Escaneo, string Usuario)> Insertados { get; } = new List<(EscaneoAlmacenDTO, string)>();
        public int Bloqueos { get; private set; }

        public Task<bool> BloquearYMirarSiTerminada(string empresa, string tipoOrigen, int numero)
        {
            Bloqueos++;
            return Task.FromResult(Terminada);
        }

        public Task<bool> ExisteEscaneo(Guid idCliente) => Task.FromResult(Insertados.Any(i => i.Escaneo.IdCliente == idCliente));

        public Task<List<LecturaPickingAlmacen>> LeerLecturas(string empresa, string tipoOrigen, int numero) => Task.FromResult(Lecturas.ToList());

        public Task<bool> InsertarEscaneo(string empresa, EscaneoAlmacenDTO escaneo, string usuario)
        {
            Insertados.Add((escaneo, usuario));
            bool falta = escaneo.Metodo == CasadorEscaneos.METODO_FALTA;
            Lecturas.Add(new LecturaPickingAlmacen
            {
                Producto = escaneo.Producto,
                Usuario = usuario,
                Unidades = falta ? 0 : escaneo.Cantidad,
                Faltas = falta ? escaneo.Cantidad : 0
            });
            return Task.FromResult(true);
        }
    }

    public class RepositorioCorreccionesFalso : IRepositorioCorrecciones
    {
        public TransaccionCorreccionFalsa Transaccion { get; } = new TransaccionCorreccionFalsa();

        public Task<T> EnTransaccion<T>(Func<ITransaccionCorreccion, Task<T>> trabajo) => trabajo(Transaccion);
    }

    /// <summary>
    /// NestoAPI#646 / Ariadna#41: corregir una parada (quitar una falta o lo cogido) desde cualquier PDA, de forma atómica: solo
    /// si la parada sigue como la vio la app. Así dos PDA que corrigen a la vez no dejan la falta en negativo.
    /// </summary>
    [TestClass]
    public class CorregirLecturaSalidaTests
    {
        private const string EMPRESA = "1";
        private IRepositorioPreparacionAlmacen repositorio;
        private RepositorioCorreccionesFalso correcciones;
        private TransaccionCorreccionFalsa tx;
        private ServicioSalidas servicio;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioPreparacionAlmacen>();
            correcciones = new RepositorioCorreccionesFalso();
            tx = correcciones.Transaccion;
            servicio = new ServicioSalidas(new IOrigenSalida[]
            {
                new OrigenSalidaPicking(repositorio),
                new OrigenSalidaReposicion(repositorio)
            }, correcciones: correcciones);
            // Picking 99739: A en el hueco 001/001/001 (3) y B en el 002/001/001 (2). Pedro ha cogido 2 de A y ha dado 1 por falta.
            A.CallTo(() => repositorio.LeerLineasPicking(EMPRESA, 99739)).Returns(new List<LineaPickingAlmacenDTO>
            {
                new LineaPickingAlmacenDTO { Producto = "A", Descripcion = "CREMA A", Cantidad = 3, Pasillo = "001", Fila = "001", Columna = "001" },
                new LineaPickingAlmacenDTO { Producto = "B", Descripcion = "CREMA B", Cantidad = 2, Pasillo = "002", Fila = "001", Columna = "001" }
            });
            tx.Lecturas = new List<LecturaPickingAlmacen>
            {
                new LecturaPickingAlmacen { Producto = "A", Usuario = "Pedro", Unidades = 2, Faltas = 1 }
            };
        }

        private static IPrincipal Usuario()
            => new GenericPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Andre") }, "prueba"), new[] { "Almacén" });

        private static CorregirEscaneoDTO QuitarFalta(int cogidas = 2, int faltas = 1, int unidades = 1, Guid? id = null) => new CorregirEscaneoDTO
        {
            IdCliente = id ?? Guid.NewGuid(),
            TipoOrigen = "PICK",
            NumeroOrigen = 99739,
            Producto = "A",
            Hueco = "001/001/001",
            Orden = 1,
            Tipo = "FALTA",
            Unidades = unidades,
            EsperadoCogidas = cogidas,
            EsperadoFaltas = faltas,
            Dispositivo = "PDA-2"
        };

        [TestMethod]
        public async Task QuitarFalta_ParadaComoLaVioLaApp_GuardaLaFaltaEnNegativoConSuHueco()
        {
            ResultadoCorreccionLectura resultado = await servicio.CorregirLectura(EMPRESA, QuitarFalta(), Usuario());

            Assert.AreEqual(EstadoCorreccionLectura.Corregida, resultado.Estado);
            (EscaneoAlmacenDTO escaneo, string usuario) = tx.Insertados.Single();
            Assert.AreEqual(-1, escaneo.Cantidad);
            Assert.AreEqual("FALTA", escaneo.Metodo);
            Assert.AreEqual("001/001/001", escaneo.Motivo, "Con el hueco, como un «Deshacer» de la falta");
            Assert.AreEqual("PICK", escaneo.Fase);
            Assert.AreEqual("Andre", usuario);
            Assert.AreEqual("PDA-2", escaneo.Dispositivo);
            Assert.AreEqual(0, resultado.Respuesta.Faltas, "La parada como queda");
            Assert.AreEqual(2, resultado.Respuesta.Cogidas);
            Assert.AreEqual(1, resultado.Respuesta.Linea.Pendiente);
            Assert.AreEqual(1, tx.Bloqueos, "Todo con las lecturas de la salida bloqueadas");
        }

        [TestMethod]
        public async Task DosPdaQuitanLaMismaFaltaALaVez_LaSegundaNoTocaNadaYLaFaltaNoQuedaEnNegativo()
        {
            // Las dos vieron la parada con 2 cogidas y 1 en falta
            ResultadoCorreccionLectura primera = await servicio.CorregirLectura(EMPRESA, QuitarFalta(), Usuario());
            ResultadoCorreccionLectura segunda = await servicio.CorregirLectura(EMPRESA, QuitarFalta(), Usuario());

            Assert.AreEqual(EstadoCorreccionLectura.Corregida, primera.Estado);
            Assert.AreEqual(EstadoCorreccionLectura.HaCambiado, segunda.Estado);
            Assert.AreEqual(1, tx.Insertados.Count);
            Assert.AreEqual(0, tx.Lecturas.Sum(l => l.Faltas), "Ni una falta en negativo");
            Assert.AreEqual(0, segunda.Respuesta.Faltas, "Con el 409 va cómo está ahora");
            StringAssert.Contains(segunda.Respuesta.Mensaje, "ha cambiado");
        }

        [TestMethod]
        public async Task ReenvioConElMismoIdCliente_NoCorrigeDosVeces()
        {
            Guid id = Guid.NewGuid();
            _ = await servicio.CorregirLectura(EMPRESA, QuitarFalta(id: id), Usuario());

            ResultadoCorreccionLectura otra = await servicio.CorregirLectura(EMPRESA, QuitarFalta(id: id), Usuario());

            Assert.AreEqual(EstadoCorreccionLectura.Repetida, otra.Estado);
            Assert.IsTrue(otra.Respuesta.Corregida);
            Assert.AreEqual(1, tx.Insertados.Count);
        }

        [TestMethod]
        public async Task SalidaYaTerminada_NoSeTocaNada()
        {
            tx.Terminada = true;

            ResultadoCorreccionLectura resultado = await servicio.CorregirLectura(EMPRESA, QuitarFalta(), Usuario());

            Assert.AreEqual(EstadoCorreccionLectura.YaTerminada, resultado.Estado);
            Assert.IsTrue(resultado.Respuesta.YaTerminada);
            Assert.AreEqual(0, tx.Insertados.Count);
        }

        [TestMethod]
        public async Task QuitarCogidas_VaConElMetodoDeLaLectura()
        {
            CorregirEscaneoDTO correccion = QuitarFalta();
            correccion.Tipo = "LECTURA";
            correccion.Unidades = 2;
            correccion.Metodo = "etiq";

            ResultadoCorreccionLectura resultado = await servicio.CorregirLectura(EMPRESA, correccion, Usuario());

            Assert.AreEqual(EstadoCorreccionLectura.Corregida, resultado.Estado);
            EscaneoAlmacenDTO escaneo = tx.Insertados.Single().Escaneo;
            Assert.AreEqual(-2, escaneo.Cantidad);
            Assert.AreEqual("ETIQ", escaneo.Metodo);
            Assert.IsNull(escaneo.Motivo);
            Assert.AreEqual(0, resultado.Respuesta.Cogidas);
        }

        [TestMethod]
        public async Task QuitarMasDeLoQueHay_NoValida()
        {
            ResultadoCorreccionLectura resultado = await servicio.CorregirLectura(EMPRESA, QuitarFalta(unidades: 2), Usuario());

            Assert.AreEqual(EstadoCorreccionLectura.NoValida, resultado.Estado);
            Assert.AreEqual(0, tx.Insertados.Count);
        }

        [TestMethod]
        public async Task ParadaQueYaNoEsta_HaCambiado()
        {
            CorregirEscaneoDTO correccion = QuitarFalta();
            correccion.Orden = 7;

            ResultadoCorreccionLectura resultado = await servicio.CorregirLectura(EMPRESA, correccion, Usuario());

            Assert.AreEqual(EstadoCorreccionLectura.HaCambiado, resultado.Estado);
            Assert.IsNull(resultado.Respuesta.Linea);
        }

        [TestMethod]
        public void MotivoDeRechazoCorreccion_TipoYUnidades()
        {
            Assert.IsNull(ServicioSalidas.MotivoDeRechazoCorreccion(QuitarFalta()));
            CorregirEscaneoDTO malTipo = QuitarFalta(); malTipo.Tipo = "OTRO";
            CorregirEscaneoDTO sinUnidades = QuitarFalta(unidades: 0);
            CorregirEscaneoDTO sinId = QuitarFalta(id: Guid.Empty);
            StringAssert.Contains(ServicioSalidas.MotivoDeRechazoCorreccion(malTipo), "FALTA o LECTURA");
            StringAssert.Contains(ServicioSalidas.MotivoDeRechazoCorreccion(sinUnidades), "más de 0");
            StringAssert.Contains(ServicioSalidas.MotivoDeRechazoCorreccion(sinId), "IdCliente");
            Assert.IsNotNull(ServicioSalidas.MotivoDeRechazoCorreccion(null));
        }

        [TestMethod]
        public async Task Controlador_HaCambiado_Es409ConComoEstaAhora()
        {
            IServicioPreparacionAlmacen preparacion = A.Fake<IServicioPreparacionAlmacen>();
            var respuesta = new ResultadoCorregirEscaneoDTO { Mensaje = "La parada ha cambiado", Faltas = 0 };
            A.CallTo(() => preparacion.CorregirLectura("1", A<CorregirEscaneoDTO>._, A<IPrincipal>._))
                .Returns(new ResultadoCorreccionLectura { Estado = EstadoCorreccionLectura.HaCambiado, Respuesta = respuesta });
            var controller = new AlmacenController(preparacion, A.Fake<IServicioUbicacionesAlmacen>(), A.Fake<IServicioRecepcionCompras>(),
                A.Fake<IServicioRecepcionReposiciones>())
            {
                User = Usuario(),
                Request = new HttpRequestMessage()
            };

            IHttpActionResult resultado = await controller.PostCorregirEscaneo(QuitarFalta());

            var conflicto = (NegotiatedContentResult<ResultadoCorregirEscaneoDTO>)resultado;
            Assert.AreEqual(HttpStatusCode.Conflict, conflicto.StatusCode);
            Assert.AreSame(respuesta, conflicto.Content);
        }
    }
}
