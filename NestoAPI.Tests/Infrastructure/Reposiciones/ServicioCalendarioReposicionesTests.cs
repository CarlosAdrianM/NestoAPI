using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Reposiciones
{
    /// <summary>NestoAPI#577 (corte 1): GET ProximaLlegada, GET/PUT Calendario y el servicio que hay detrás.</summary>
    [TestClass]
    public class ServicioCalendarioReposicionesTests
    {
        private static readonly DateTime LUNES_0900 = new DateTime(2026, 10, 19, 9, 0, 0);

        private NVEntities db;
        private DbSet<ReposicionCalendario> fakeCalendario;
        private DbSet<ParametroUsuario> fakeParametros;
        private List<ReposicionCalendario> filas;
        private List<ParametroUsuario> parametros;

        [TestInitialize]
        public void Preparar()
        {
            db = A.Fake<NVEntities>();
            filas = new List<ReposicionCalendario>();
            parametros = new List<ParametroUsuario>();
            fakeCalendario = A.Fake<DbSet<ReposicionCalendario>>(o => o
                .Implements<IQueryable<ReposicionCalendario>>()
                .Implements<IDbAsyncEnumerable<ReposicionCalendario>>());
            fakeParametros = A.Fake<DbSet<ParametroUsuario>>(o => o
                .Implements<IQueryable<ParametroUsuario>>()
                .Implements<IDbAsyncEnumerable<ParametroUsuario>>());
            ConfigurarFakeDbSet(fakeCalendario, filas);
            ConfigurarFakeDbSet(fakeParametros, parametros);
            A.CallTo(() => db.ReposicionesCalendario).Returns(fakeCalendario);
            A.CallTo(() => db.ParametrosUsuario).Returns(fakeParametros);
        }

        private static ReposicionCalendario Fila(int id, string origen, byte dia, string cierre = "10:00", string llegada = "13:30",
            string destino = "ALG", byte antelacion = 0) =>
            new ReposicionCalendario
            {
                Id = id,
                Empresa = "1",
                AlmacenOrigen = origen,
                AlmacenDestino = destino,
                LaborablesAntelacionCierre = antelacion,
                DiaSemana = dia,
                HoraCierre = TimeSpan.Parse(cierre),
                HoraLlegadaHabitual = TimeSpan.Parse(llegada),
                Activo = true,
                Usuario = "sa",
                FechaModificacion = new DateTime(2026, 10, 7)
            };

        private ServicioCalendarioReposiciones Servicio() =>
            new ServicioCalendarioReposiciones(db, new CalculadoraFechaReposicion((d, a) => false), () => LUNES_0900);

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, nombre) };
            claims.AddRange(grupos.Select(g => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + g)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private ReposicionesController Controlador(IPrincipal usuario, IServicioCalendarioReposiciones servicio = null) =>
            new ReposicionesController(db, null, null, servicio ?? Servicio()) { Request = new HttpRequestMessage(), User = usuario };

        private static ReposicionCalendarioDTO Dto(string origen, string destino, byte dia, string cierre = "10:00", string llegada = "13:30",
            byte antelacion = 0, int? id = null, bool activo = true) =>
            new ReposicionCalendarioDTO
            {
                Id = id, Origen = origen, Destino = destino, DiaSemana = dia, HoraCierre = TimeSpan.Parse(cierre),
                HoraLlegadaHabitual = TimeSpan.Parse(llegada), LaborablesAntelacionCierre = antelacion, Activo = activo
            };

        private async Task<NestoBusinessException> GuardarFalla(params ReposicionCalendarioDTO[] filasPeticion)
        {
            NestoBusinessException error = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Guardar(
                new GuardarCalendarioReposicionesDTO { Filas = filasPeticion.ToList() }, "x"));
            Assert.AreEqual(HttpStatusCode.BadRequest, error.StatusCode);
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
            return error;
        }

        // ---------------- Hora de corte del picking ----------------

        [TestMethod]
        public void InterpretarHoraCorte_SinParametroOIlegible_LasOnce()
        {
            Assert.AreEqual(new TimeSpan(11, 0, 0), NestoAPI.Models.Picking.HoraCortePicking.Interpretar(null));
            Assert.AreEqual(new TimeSpan(11, 0, 0), NestoAPI.Models.Picking.HoraCortePicking.Interpretar(" "));
            Assert.AreEqual(new TimeSpan(11, 0, 0), NestoAPI.Models.Picking.HoraCortePicking.Interpretar("once"));
            Assert.AreEqual(new TimeSpan(11, 0, 0), NestoAPI.Models.Picking.HoraCortePicking.Interpretar("25:00"));
            Assert.AreEqual(new TimeSpan(10, 30, 0), NestoAPI.Models.Picking.HoraCortePicking.Interpretar(" 10:30 "));
        }

        [TestMethod]
        public async Task LeerProximaLlegada_UsaLaHoraDeCorteDelParametroDefecto()
        {
            filas.Add(Fila(1, "REI", 1, cierre: "10:00", llegada: "11:30"));
            parametros.Add(new ParametroUsuario { Empresa = "1", Usuario = "(defecto)", Clave = "HoraCortePicking", Valor = "12:00" });

            ProximaReposicionDTO proxima = await Servicio().LeerProximaLlegada("1", "rei", "alg");

            Assert.AreEqual(LUNES_0900.Date, proxima.PedidoSaleEl, "Llega 11:30, antes del corte de las 12:00: sale el mismo día");
        }

        [TestMethod]
        public async Task LeerProximaLlegada_SinParametro_CortaALasOnce()
        {
            filas.Add(Fila(1, "REI", 1, cierre: "10:00", llegada: "11:30"));

            ProximaReposicionDTO proxima = await Servicio().LeerProximaLlegada(null, "REI", "ALG");

            Assert.AreEqual(LUNES_0900.Date.AddDays(1), proxima.PedidoSaleEl);
        }

        // ---------------- GET ProximaLlegada ----------------

        [TestMethod]
        public async Task GetProximaLlegada_ConCalendario_200ConElDTO()
        {
            filas.Add(Fila(1, "REI", 1));

            var resultado = await Controlador(Usuario("NUEVAVISION\\Laura", "Ventas")).GetProximaLlegada("REI", "ALG")
                as OkNegotiatedContentResult<ProximaReposicionDTO>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(LUNES_0900.Date.AddHours(10), resultado.Content.CierraEl);
            Assert.AreEqual(LUNES_0900.Date.AddHours(13).AddMinutes(30), resultado.Content.LlegaEl);
            Assert.AreEqual(LUNES_0900.Date.AddDays(1), resultado.Content.PedidoSaleEl);
        }

        [TestMethod]
        public async Task GetProximaLlegada_SinCalendario_404ConMensaje()
        {
            filas.Add(Fila(1, "ALC", 1));

            var resultado = await Controlador(Usuario("NUEVAVISION\\Laura")).GetProximaLlegada("rei", "ALG")
                as NegotiatedContentResult<string>;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.NotFound, resultado.StatusCode);
            StringAssert.Contains(resultado.Content, "REI a ALG");
        }

        [TestMethod]
        public void ProximaLlegada_ElControladorExigeEstarIdentificado()
        {
            Assert.IsTrue(typeof(ReposicionesController).GetCustomAttributes(typeof(System.Web.Http.AuthorizeAttribute), true).Length > 0);
        }

        // ---------------- GET / PUT Calendario ----------------

        [TestMethod]
        public async Task GetCalendario_OrdenadoPorRutaDiaYHora()
        {
            filas.Add(Fila(2, "REI", 3));
            filas.Add(Fila(1, "REI", 1));
            filas.Add(Fila(3, "ALC", 2));

            var resultado = await Controlador(Usuario("NUEVAVISION\\Laura")).GetCalendario()
                as OkNegotiatedContentResult<List<ReposicionCalendarioDTO>>;

            CollectionAssert.AreEqual(new int?[] { 3, 1, 2 }, resultado.Content.Select(f => f.Id).ToArray());
        }

        [TestMethod]
        public async Task PutCalendario_FueraDeLaListaDeRellenarAMano_403YNoGuarda()
        {
            var servicio = A.Fake<IServicioCalendarioReposiciones>();
            var controlador = Controlador(Usuario("NUEVAVISION\\Paloma", "Almacén"), servicio);

            var resultado = await controlador.PutCalendario(new GuardarCalendarioReposicionesDTO { Filas = new List<ReposicionCalendarioDTO>() })
                as ResponseMessageResult;

            Assert.IsNotNull(resultado);
            Assert.AreEqual(HttpStatusCode.Forbidden, resultado.Response.StatusCode, "Ser de Almacén ya no basta: la misma lista que rellenar a mano");
            StringAssert.Contains(await resultado.Response.Content.ReadAsStringAsync(), "personas autorizadas");
            A.CallTo(() => servicio.Guardar(A<GuardarCalendarioReposicionesDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutCalendario_DeLaListaDeRellenarAMano_GuardaConElUsuarioDelIdentity()
        {
            var servicio = A.Fake<IServicioCalendarioReposiciones>();
            var peticion = new GuardarCalendarioReposicionesDTO { Filas = new List<ReposicionCalendarioDTO>() };

            var resultado = await Controlador(Usuario("NUEVAVISION\\Alfredo"), servicio).PutCalendario(peticion)
                as OkNegotiatedContentResult<List<ReposicionCalendarioDTO>>;

            Assert.IsNotNull(resultado, "Alfredo está en la lista por defecto (sin fila del parámetro) aunque no tenga grupos");
            A.CallTo(() => servicio.Guardar(peticion, "NUEVAVISION\\Alfredo")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PutCalendario_ListaDelParametroDefecto_SoloLosQueEstan()
        {
            parametros.Add(new ParametroUsuario { Empresa = "1", Usuario = "(defecto)", Clave = PermisoRellenarReposicionManual.CLAVE, Valor = "Manuel" });
            var servicio = A.Fake<IServicioCalendarioReposiciones>();
            var peticion = new GuardarCalendarioReposicionesDTO { Filas = new List<ReposicionCalendarioDTO>() };

            var deCarlos = await Controlador(Usuario("NUEVAVISION\\Carlos", "Informática"), servicio).PutCalendario(peticion) as ResponseMessageResult;
            var deManuel = await Controlador(Usuario("manuel"), servicio).PutCalendario(peticion) as OkNegotiatedContentResult<List<ReposicionCalendarioDTO>>;

            Assert.AreEqual(HttpStatusCode.Forbidden, deCarlos.Response.StatusCode);
            Assert.IsNotNull(deManuel);
        }

        [TestMethod]
        public void GetPuedeEditarCalendario_LaListaDeRellenarAMano()
        {
            var si = Controlador(Usuario("NUEVAVISION\\Manuel")).GetPuedeEditarCalendario() as OkNegotiatedContentResult<bool>;
            var no = Controlador(Usuario("NUEVAVISION\\Paloma", "Almacén", "Dirección")).GetPuedeEditarCalendario() as OkNegotiatedContentResult<bool>;

            Assert.IsTrue(si.Content);
            Assert.IsFalse(no.Content);
        }

        [TestMethod]
        public async Task GetCalendario_CualquierIdentificadoLoLee()
        {
            filas.Add(Fila(1, "REI", 1));

            var resultado = await Controlador(Usuario("NUEVAVISION\\Paloma", "Tiendas")).GetCalendario("1")
                as OkNegotiatedContentResult<List<ReposicionCalendarioDTO>>;

            Assert.AreEqual(1, resultado.Content.Single().Id);
            Assert.AreEqual("REI", resultado.Content.Single().Origen);
        }

        [TestMethod]
        public void PuedeMantener_AlmacenDireccionEInformatica()
        {
            Assert.IsTrue(ServicioCalendarioReposiciones.PuedeMantener(Usuario("a", "Almacén")));
            Assert.IsTrue(ServicioCalendarioReposiciones.PuedeMantener(Usuario("b", "Dirección")));
            Assert.IsTrue(ServicioCalendarioReposiciones.PuedeMantener(Usuario("c", "Informática")));
            Assert.IsFalse(ServicioCalendarioReposiciones.PuedeMantener(Usuario("d", "Tiendas", "Ventas")));
            Assert.IsFalse(ServicioCalendarioReposiciones.PuedeMantener(null));
        }

        [TestMethod]
        public async Task Guardar_RutaCompleta_CambiaLaQueCoincideCreaLaNuevaYDesactivaLaQueSobra()
        {
            ReposicionCalendario lunes = Fila(1, "REI", 1);
            ReposicionCalendario miercoles = Fila(2, "REI", 3);
            ReposicionCalendario deAlcobendas = Fila(3, "ALC", 2);
            filas.AddRange(new[] { lunes, miercoles, deAlcobendas });

            await Servicio().Guardar(new GuardarCalendarioReposicionesDTO
            {
                Origen = "rei",
                Destino = "ALG",
                Filas = new List<ReposicionCalendarioDTO>
                {
                    new ReposicionCalendarioDTO { DiaSemana = 1, HoraCierre = TimeSpan.Parse("10:00"), HoraLlegadaHabitual = TimeSpan.Parse("14:00"), Activo = true },
                    new ReposicionCalendarioDTO { DiaSemana = 5, HoraCierre = TimeSpan.Parse("10:00"), HoraLlegadaHabitual = TimeSpan.Parse("13:30"), Activo = true }
                }
            }, "NUEVAVISION\\Almacen1");

            Assert.AreEqual(TimeSpan.Parse("14:00"), lunes.HoraLlegadaHabitual, "La del lunes coincide por día: se cambia");
            Assert.AreEqual("Almacen1", lunes.Usuario, "Usuario sin dominio");
            Assert.AreEqual(LUNES_0900, lunes.FechaModificacion);
            A.CallTo(() => fakeCalendario.Add(A<ReposicionCalendario>.That.Matches(f =>
                f.DiaSemana == 5 && f.AlmacenOrigen == "REI" && f.AlmacenDestino == "ALG" && f.Empresa == "1" && f.Usuario == "Almacen1")))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => fakeCalendario.Remove(A<ReposicionCalendario>._)).MustNotHaveHappened();
            Assert.IsFalse(miercoles.Activo, "La que sobra se desactiva, no se borra");
            Assert.AreEqual("Almacen1", miercoles.Usuario);
            Assert.AreEqual(LUNES_0900, miercoles.FechaModificacion);
            Assert.IsTrue(deAlcobendas.Activo);
            Assert.AreEqual("sa", deAlcobendas.Usuario, "Las demás rutas no se tocan");
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Guardar_FilasSueltas_NoBorraNada()
        {
            ReposicionCalendario lunes = Fila(1, "REI", 1);
            filas.Add(lunes);

            await Servicio().Guardar(new GuardarCalendarioReposicionesDTO
            {
                Filas = new List<ReposicionCalendarioDTO>
                {
                    new ReposicionCalendarioDTO { Id = 1, Origen = "REI", Destino = "ALG", DiaSemana = 1, HoraCierre = TimeSpan.Parse("10:00"), HoraLlegadaHabitual = TimeSpan.Parse("13:30"), Activo = false }
                }
            }, null);

            Assert.IsFalse(lunes.Activo);
            Assert.AreEqual("DESCONOCIDO", lunes.Usuario);
            A.CallTo(() => fakeCalendario.Remove(A<ReposicionCalendario>._)).MustNotHaveHappened();
            A.CallTo(() => fakeCalendario.Add(A<ReposicionCalendario>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Guardar_FilasQueNoValen_400SinGuardar()
        {
            async Task Falla(ReposicionCalendarioDTO fila, string origen = null, string destino = null)
            {
                await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Guardar(new GuardarCalendarioReposicionesDTO
                {
                    Origen = origen,
                    Destino = destino,
                    Filas = new List<ReposicionCalendarioDTO> { fila }
                }, "x"));
            }
            TimeSpan diez = TimeSpan.Parse("10:00");
            TimeSpan unaYMedia = TimeSpan.Parse("13:30");

            await Falla(new ReposicionCalendarioDTO { Origen = "REI", Destino = "ALG", DiaSemana = 8, HoraCierre = diez, HoraLlegadaHabitual = unaYMedia });
            await Falla(new ReposicionCalendarioDTO { Origen = "ALG", Destino = "ALG", DiaSemana = 1, HoraCierre = diez, HoraLlegadaHabitual = unaYMedia });
            await Falla(new ReposicionCalendarioDTO { Origen = "REI", Destino = "ALG", DiaSemana = 1, HoraCierre = unaYMedia, HoraLlegadaHabitual = diez });
            await Falla(new ReposicionCalendarioDTO { Origen = "", Destino = "ALG", DiaSemana = 1, HoraCierre = diez, HoraLlegadaHabitual = unaYMedia });
            await Falla(new ReposicionCalendarioDTO { Origen = "ALC", Destino = "ALG", DiaSemana = 1, HoraCierre = diez, HoraLlegadaHabitual = unaYMedia }, "REI", "ALG");
            await Falla(new ReposicionCalendarioDTO { Id = 99, Origen = "REI", Destino = "ALG", DiaSemana = 1, HoraCierre = diez, HoraLlegadaHabitual = unaYMedia });
            await Falla(new ReposicionCalendarioDTO { Origen = "ALG", Destino = "REI", DiaSemana = 1, HoraCierre = diez, HoraLlegadaHabitual = unaYMedia, LaborablesAntelacionCierre = 6 });
            A.CallTo(() => db.SaveChangesAsync()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Guardar_ConAntelacion_LaLlegadaPuedeSerAnteriorAlCierreYSeGuardaLaAntelacion()
        {
            List<ReposicionCalendarioDTO> leidas = await Servicio().Guardar(new GuardarCalendarioReposicionesDTO
            {
                Filas = new List<ReposicionCalendarioDTO>
                {
                    new ReposicionCalendarioDTO { Origen = "ALG", Destino = "REI", DiaSemana = 1, HoraCierre = TimeSpan.Parse("13:00"),
                        HoraLlegadaHabitual = TimeSpan.Parse("11:00"), LaborablesAntelacionCierre = 1, Activo = true }
                }
            }, "x");

            A.CallTo(() => fakeCalendario.Add(A<ReposicionCalendario>.That.Matches(f =>
                f.AlmacenOrigen == "ALG" && f.AlmacenDestino == "REI" && f.LaborablesAntelacionCierre == 1
                && f.HoraCierre == TimeSpan.Parse("13:00") && f.HoraLlegadaHabitual == TimeSpan.Parse("11:00"))))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Guardar_DosFilasIguales_400()
        {
            var fila = new ReposicionCalendarioDTO { Origen = "REI", Destino = "ALG", DiaSemana = 1, HoraCierre = TimeSpan.Parse("10:00"), HoraLlegadaHabitual = TimeSpan.Parse("13:30") };

            await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => Servicio().Guardar(
                new GuardarCalendarioReposicionesDTO { Filas = new List<ReposicionCalendarioDTO> { fila, fila } }, "x"));
        }

        [TestMethod]
        public async Task Guardar_CambiarLaHoraDeUnaFila_LaMismaFilaConUsuarioYFecha()
        {
            ReposicionCalendario lunes = Fila(1, "REI", 1);
            filas.Add(lunes);

            await Servicio().Guardar(new GuardarCalendarioReposicionesDTO
            {
                Filas = new List<ReposicionCalendarioDTO> { Dto("REI", "ALG", 1, cierre: "09:00", id: 1) }
            }, "NUEVAVISION\\Carlos");

            Assert.AreEqual(TimeSpan.Parse("09:00"), lunes.HoraCierre);
            Assert.AreEqual("Carlos", lunes.Usuario);
            Assert.AreEqual(LUNES_0900, lunes.FechaModificacion);
            A.CallTo(() => fakeCalendario.Add(A<ReposicionCalendario>._)).MustNotHaveHappened();
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Guardar_AnadirUnDiaQueEstabaDesactivado_LoReactivaSinCrearOtro()
        {
            ReposicionCalendario martes = Fila(1, "REI", 2);
            martes.Activo = false;
            filas.Add(martes);

            await Servicio().Guardar(new GuardarCalendarioReposicionesDTO
            {
                Filas = new List<ReposicionCalendarioDTO> { Dto("REI", "ALG", 2, cierre: "09:30") }
            }, "Alfredo");

            Assert.IsTrue(martes.Activo);
            Assert.AreEqual(TimeSpan.Parse("09:30"), martes.HoraCierre);
            A.CallTo(() => fakeCalendario.Add(A<ReposicionCalendario>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Guardar_AnadirUnDiaNuevo_LoCrea()
        {
            filas.Add(Fila(1, "REI", 1));

            await Servicio().Guardar(new GuardarCalendarioReposicionesDTO
            {
                Filas = new List<ReposicionCalendarioDTO> { Dto("REI", "ALG", 2) }
            }, "Alfredo");

            A.CallTo(() => fakeCalendario.Add(A<ReposicionCalendario>.That.Matches(f => f.DiaSemana == 2 && f.Activo && f.Usuario == "Alfredo")))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Guardar_MoverUnaFilaAUnDiaQueYaTieneLaRuta_400()
        {
            filas.Add(Fila(1, "REI", 1));
            filas.Add(Fila(2, "REI", 3));

            NestoBusinessException error = await GuardarFalla(Dto("REI", "ALG", 3, id: 1));

            Assert.AreEqual("Ya hay una reposición de REI a ALG que llega el miércoles.", error.Message);
        }

        [TestMethod]
        public async Task Guardar_MoverUnaFilaAUnDiaDesactivado_400DiceQueLaActive()
        {
            filas.Add(Fila(1, "REI", 1));
            ReposicionCalendario miercoles = Fila(2, "REI", 3);
            miercoles.Activo = false;
            filas.Add(miercoles);

            NestoBusinessException error = await GuardarFalla(Dto("REI", "ALG", 3, id: 1));

            StringAssert.Contains(error.Message, "actívala en vez de crear otra");
        }

        [TestMethod]
        public async Task Guardar_AntelacionDistintaEnLaMismaRuta_400()
        {
            filas.Add(Fila(1, "ALG", 1, "13:00", "11:00", destino: "REI", antelacion: 1));
            filas.Add(Fila(2, "ALG", 3, "13:00", "11:00", destino: "REI", antelacion: 1));

            NestoBusinessException error = await GuardarFalla(Dto("ALG", "REI", 1, "13:00", "11:00", antelacion: 2, id: 1));

            StringAssert.Contains(error.Message, "la misma antelación");
        }

        [TestMethod]
        public async Task Guardar_AntelacionCambiadaEnTodaLaRutaALaVez_SeGuarda()
        {
            ReposicionCalendario lunes = Fila(1, "ALG", 1, "13:00", "11:00", destino: "REI", antelacion: 1);
            ReposicionCalendario miercoles = Fila(2, "ALG", 3, "13:00", "11:00", destino: "REI", antelacion: 1);
            ReposicionCalendario viernes = Fila(3, "ALG", 5, "13:00", "11:00", destino: "REI", antelacion: 1);
            viernes.Activo = false; // las desactivadas no cuentan
            filas.AddRange(new[] { lunes, miercoles, viernes });

            await Servicio().Guardar(new GuardarCalendarioReposicionesDTO
            {
                Filas = new List<ReposicionCalendarioDTO>
                {
                    Dto("ALG", "REI", 1, "13:00", "11:00", antelacion: 2, id: 1),
                    Dto("ALG", "REI", 3, "13:00", "11:00", antelacion: 2, id: 2)
                }
            }, "Carlos");

            Assert.AreEqual(2, lunes.LaborablesAntelacionCierre);
            Assert.AreEqual(2, miercoles.LaborablesAntelacionCierre);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Guardar_AlmacenQueNoHaceReposiciones_400()
        {
            NestoBusinessException error = await GuardarFalla(Dto("MAD", "ALG", 1));

            StringAssert.Contains(error.Message, "ALG, REI, ALC");
        }

        [TestMethod]
        public async Task Guardar_ConAntelacionCeroElCierreDespuesDeLaLlegada_400ConMensajeClaro()
        {
            NestoBusinessException error = await GuardarFalla(Dto("REI", "ALG", 1, cierre: "14:00", llegada: "13:30"));

            StringAssert.Contains(error.Message, "La hora de llegada no puede ser anterior a la hora de cierre");
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, List<T> datos) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
        }
    }
}
