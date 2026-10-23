using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Ubicaciones;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// Ariadna#12 (parte API): en la salida de una reposición desde Algete, «No está» en un hueco y hay en otro: la reserva
    /// de la reposición (estado 4) pasa a ese hueco, como en el picking. Y las rutas por tipo (Recogidas/{tipo}/{n}/…),
    /// que atienden PICK igual que la ruta de siempre del picking.
    /// </summary>
    [TestClass]
    public class CambioHuecoReposicionTests
    {
        private const int TRASPASO = 81143;

        // ---------------------------------------------------------------- El plan (puro)

        private static FilaUbicacionReposicion Reserva(int numeroOrden, int cantidad, int estado = EstadosUbicacion.RESERVA_REPOSICION, int? repo = 562380800)
        {
            return new FilaUbicacionReposicion
            {
                NumeroOrden = numeroOrden, Empresa = "1", Almacen = "ALG", Producto = "10342", Pasillo = "001", Fila = "004", Columna = "003",
                Cantidad = cantidad, Estado = estado, NumeroOrdenRepo = repo
            };
        }

        private static FilaUbicacionLibre Libre(int numeroOrden, int cantidad, int estado = EstadosUbicacion.LIBRE)
        {
            return new FilaUbicacionLibre
            {
                NumeroOrden = numeroOrden, Empresa = "1", Almacen = "ALG", Producto = "10342", Pasillo = "003", Fila = "001", Columna = "002",
                Cantidad = cantidad, Estado = estado
            };
        }

        [TestMethod]
        public void Planear_LoQueNoEstaPasaDeLaReservaALaLibreDelOtroHueco()
        {
            List<TrasladoDeReserva> plan = PlanificadorCambioHuecoReposicion.Planear(new[] { Reserva(10, 3) }, new[] { Libre(20, 8) }, 2, out string motivo);

            TrasladoDeReserva traslado = plan.Single();
            Assert.AreEqual(10, traslado.Reserva.NumeroOrden);
            Assert.AreEqual(20, traslado.Libre.NumeroOrden);
            Assert.AreEqual(2, traslado.Cantidad, "Solo lo que no se ha encontrado; lo demás sigue reservado en el origen");
            Assert.IsNull(motivo);
        }

        [TestMethod]
        public void Planear_VariasFilasLibres_SeCogenEnOrdenSinPasarseDeNinguna()
        {
            List<TrasladoDeReserva> plan = PlanificadorCambioHuecoReposicion.Planear(
                new[] { Reserva(10, 3), Reserva(11, 2) }, new[] { Libre(20, 2), Libre(21, 4) }, 5, out _);

            CollectionAssert.AreEqual(new[] { "10>20:2", "10>21:1", "11>21:2" },
                plan.Select(t => $"{t.Reserva.NumeroOrden}>{t.Libre.NumeroOrden}:{t.Cantidad}").ToArray());
        }

        [TestMethod]
        public void Planear_SinBastanteLibreEnElDestino_NoSePlaneaNadaYDiceCuantoHay()
        {
            // A diferencia del picking (que cambia lo que haya), en la reposición es todo o nada
            List<TrasladoDeReserva> plan = PlanificadorCambioHuecoReposicion.Planear(new[] { Reserva(10, 5) }, new[] { Libre(20, 3) }, 5, out string motivo);

            Assert.IsNull(plan);
            StringAssert.Contains(motivo, "solo hay 3 libres");
        }

        [TestMethod]
        public void Planear_SinNadaLibreEnElDestino_NoSePlaneaNada()
        {
            Assert.IsNull(PlanificadorCambioHuecoReposicion.Planear(new[] { Reserva(10, 5) }, new FilaUbicacionLibre[0], 1, out string motivo));
            StringAssert.Contains(motivo, "ya no hay nada libre");
        }

        [TestMethod]
        public void Planear_SiLaReservaYaNoEstaEnEseHueco_NoSePlaneaNada()
        {
            // Otra PDA ya la ha cambiado, o la salida ya ha pasado a -4 (terminada)
            Assert.IsNull(PlanificadorCambioHuecoReposicion.Planear(new FilaUbicacionReposicion[0], new[] { Libre(20, 8) }, 1, out string motivo));
            StringAssert.Contains(motivo, "ya no tiene nada reservado");
            Assert.IsNull(PlanificadorCambioHuecoReposicion.Planear(new[] { Reserva(10, 2, EstadosUbicacion.SALIDA_REPOSICION) }, new[] { Libre(20, 8) }, 1, out _),
                "Una salida ya contabilizada (-4) no se mueve");
        }

        [TestMethod]
        public void Planear_NuncaMasDeLoReservadoEnElOrigen()
        {
            Assert.IsNull(PlanificadorCambioHuecoReposicion.Planear(new[] { Reserva(10, 2) }, new[] { Libre(20, 50) }, 3, out string motivo));
            StringAssert.Contains(motivo, "solo tiene reservadas 2");
        }

        [TestMethod]
        public void Planear_SoloCuentaLoLibreDeVerdad()
        {
            // Lo pendiente de ubicar (2) no tiene hueco: no se ofrece ni se coge
            Assert.IsNull(PlanificadorCambioHuecoReposicion.Planear(new[] { Reserva(10, 2) }, new[] { Libre(20, 9, EstadosUbicacion.PENDIENTE_DE_UBICAR) }, 1, out _));
        }

        [TestMethod]
        public void Planear_CuadraElStockPorHueco()
        {
            // Antes: origen 001/004/003 con 3 reservadas (y 2 que no están), destino 003/001/002 con 8 libres.
            // Después: origen 1 reservada; destino 6 libres + 2 reservadas = 8; «pendiente de ubicar» +2. Total del almacén igual.
            FilaUbicacionReposicion reserva = Reserva(10, 3);
            FilaUbicacionLibre libre = Libre(20, 8);
            List<TrasladoDeReserva> plan = PlanificadorCambioHuecoReposicion.Planear(new[] { reserva }, new[] { libre }, 2, out _);

            int movidas = plan.Sum(t => t.Cantidad);
            int origenDespues = reserva.Cantidad - movidas;
            int destinoDespues = (libre.Cantidad - movidas) + movidas;
            int pendienteDeUbicar = movidas;
            Assert.AreEqual(1, origenDespues);
            Assert.AreEqual(libre.Cantidad, destinoDespues, "El hueco de destino tiene lo mismo, ahora una parte reservada");
            Assert.AreEqual(reserva.Cantidad + libre.Cantidad, origenDespues + destinoDespues + pendienteDeUbicar, "El almacén tiene lo mismo");
            Assert.AreEqual(reserva.Cantidad, origenDespues + movidas, "Lo reservado para el traspaso no cambia: lo leído y lo esperado siguen valiendo");
        }

        // ---------------------------------------------------------------- El SQL

        [TestMethod]
        public void Sql_Alternativas_SoloLoLibreDelAlmacenDeLaSalidaMenosElHuecoDeOrigen()
        {
            string sql = RepositorioCambioHuecoReposicionSql.SQL_ALTERNATIVAS;
            // Ni lo reservado ni lo de pedidos o albaranes
            StringAssert.Contains(sql, "u.Estado = 0 AND u.PedidoVta IS NULL AND u.[AlbaránVta] IS NULL");
            StringAssert.Contains(sql, "u.[NºOrdenVta] IS NULL AND u.[NºOrdenRepo] IS NULL");
            // Ni el hueco donde no estaba
            StringAssert.Contains(sql, "RTRIM(u.Pasillo) + RTRIM(u.Fila) + RTRIM(u.Columna) <> @p3");
            // Del almacén de la salida del traspaso, y solo si el producto va en ella
            StringAssert.Contains(sql, "s.[NºTraspaso] = @p1 AND s.Diario = a.DiarioSalidaRep AND s.Cantidad < 0");
            StringAssert.Contains(sql, "AND s.[Número] = @p2");
            Assert.IsFalse(sql.Contains("ORDER BY"), "El orden lo pone el recorrido (NestoAPI#641), como en el picking");
        }

        [TestMethod]
        public void Sql_Puerta_LeeLaReservaDelTraspasoEnElHuecoYLoLibreDelOtroBloqueados()
        {
            string reservas = PuertaUbicacionesSql.SQL_RESERVAS_DEL_TRASPASO_EN_EL_HUECO;
            StringAssert.Contains(reservas, "WITH (UPDLOCK)");
            StringAssert.Contains(reservas, "[NºTraspasoRepo] = @p0 AND Estado = 4 AND [Almacén] = @p1 AND [Número] = @p2");
            StringAssert.Contains(reservas, "RTRIM(Pasillo) + RTRIM(Fila) + RTRIM(Columna) = @p3");

            string libres = PuertaUbicacionesSql.SQL_LIBRES_DEL_HUECO;
            StringAssert.Contains(libres, "WITH (UPDLOCK, HOLDLOCK)");
            StringAssert.Contains(libres, "Estado = 0 AND Cantidad > 0");
            StringAssert.Contains(libres, "PedidoVta IS NULL AND [AlbaránVta] IS NULL AND [NºOrdenVta] IS NULL AND [NºOrdenRepo] IS NULL");
        }

        [TestMethod]
        public void Sql_Puerta_EnElDestinoLaLibrePasaAReservaDelTraspasoConLaLineaDeLaOriginal()
        {
            string sql = PuertaUbicacionesSql.SQL_RESERVA_DE_LA_LIBRE_PARA_EL_TRASPASO;
            StringAssert.Contains(sql, "IF EXISTS (SELECT 1 FROM Ubicaciones WHERE [NºOrden] = @p0 AND Estado = 0 AND Cantidad >= @p1)",
                "Si la libre ya no tiene esas unidades no se toca nada");
            StringAssert.Contains(sql, "SET Cantidad = Cantidad - @p1");
            StringAssert.Contains(sql, "Pasillo, Fila, Columna, 4, @p2, @p3, @p4 FROM Ubicaciones WHERE [NºOrden] = @p0",
                "En el mismo hueco de la libre, en estado 4, con NºOrdenRepo y NºTraspasoRepo de la reserva original");
            StringAssert.Contains(sql, "[NºOrdenRepo], [NºTraspasoRepo], Usuario");
            StringAssert.Contains(sql, "DELETE FROM Ubicaciones WHERE [NºOrden] = @p0 AND Cantidad = 0 AND Estado = 0");
            Assert.IsFalse(sql.Contains("OUTPUT"), "Ubicaciones tiene triggers: OUTPUT sin INTO falla");
            Assert.IsTrue(sql.IndexOf("SCOPE_IDENTITY", StringComparison.Ordinal) < sql.IndexOf("DELETE", StringComparison.Ordinal),
                "El NºOrden de la reserva nueva se coge antes de borrar la libre vacía");
        }

        [TestMethod]
        public void Sql_Puerta_EnElOrigenLoQueNoEstabaPasaAPendienteDeUbicar()
        {
            string sql = PuertaUbicacionesSql.SQL_RESERVA_REPOSICION_A_PENDIENTE_DE_UBICAR;
            StringAssert.Contains(sql, "WHERE [NºOrden] = @p0 AND Estado = 4 AND Cantidad >= @p1");
            StringAssert.Contains(sql, "SELECT Empresa, [Almacén], [Número], @p1, 2, @p2 FROM Ubicaciones WHERE [NºOrden] = @p0",
                "Sin hueco (estado 2), del mismo producto y almacén");
            StringAssert.Contains(sql, "SET Cantidad = Cantidad - @p1, Usuario = @p2");
            StringAssert.Contains(sql, "DELETE FROM Ubicaciones WHERE [NºOrden] = @p0 AND Cantidad = 0 AND Estado = 4");
        }

        [TestMethod]
        public void HuecoUbicacion_DeCodigo_ComoLaEtiquetaOComoSeEnsena()
        {
            Assert.AreEqual("003001002", HuecoUbicacion.DeCodigo("003/001/002").Codigo);
            Assert.AreEqual("003/001/002", HuecoUbicacion.DeCodigo("003001002").ToString());
            Assert.IsNull(HuecoUbicacion.DeCodigo("no es un hueco"));
        }

        // ---------------------------------------------------------------- El servicio

        private static CambiarHuecoPickingDTO Cambio(string origen = "001/004/003", string destino = "003001002", int cantidad = 2, string producto = " 10342 ")
        {
            return new CambiarHuecoPickingDTO { Producto = producto, HuecoOrigen = origen, HuecoDestino = destino, Cantidad = cantidad };
        }

        [TestMethod]
        public async Task Cambiar_MandaLosHuecosComoLaEtiquetaYDiceQueHaPasado()
        {
            IRepositorioCambioHuecoReposicion repositorio = A.Fake<IRepositorioCambioHuecoReposicion>();
            A.CallTo(() => repositorio.Cambiar("1", TRASPASO, "10342", "001004003", "003001002", 2, "Andrey", false))
                .Returns(new ResultadoCambioHueco { Estado = EstadoCambioHueco.Cambiado, Movidas = 2 });

            ResultadoCambioHueco resultado = await new ServicioCambioHuecoReposicion(repositorio).Cambiar("1", TRASPASO, Cambio(), "Andrey");

            Assert.AreEqual(EstadoCambioHueco.Cambiado, resultado.Estado);
            Assert.AreEqual(2, resultado.Movidas);
            Assert.AreEqual("2 unidades se cogen del hueco 003/001/002; lo que no estaba en el 001/004/003 pasa a «pendiente de ubicar».", resultado.Mensaje);
        }

        [TestMethod]
        public async Task Cambiar_SiNoSePuede_PasaElMotivo()
        {
            IRepositorioCambioHuecoReposicion repositorio = A.Fake<IRepositorioCambioHuecoReposicion>();
            A.CallTo(() => repositorio.Cambiar(A<string>._, A<int>._, A<string>._, A<string>._, A<string>._, A<int>._, A<string>._, A<bool>._))
                .Returns(new ResultadoCambioHueco { Estado = EstadoCambioHueco.NoSePuede, Mensaje = "La reposición 81143 ya está terminada: ya no se puede cambiar de hueco." });

            ResultadoCambioHueco resultado = await new ServicioCambioHuecoReposicion(repositorio).Cambiar("1", TRASPASO, Cambio(), "Andrey");

            Assert.AreEqual(EstadoCambioHueco.NoSePuede, resultado.Estado);
            StringAssert.Contains(resultado.Mensaje, "ya está terminada");
        }

        [TestMethod]
        [DataRow("", "003001002", 2, "10342")]
        [DataRow("001004003", "no es un hueco", 2, "10342")]
        [DataRow("001004003", "001/004/003", 2, "10342")]
        [DataRow("001004003", "003001002", 0, "10342")]
        [DataRow("001004003", "003001002", 2, " ")]
        public async Task Cambiar_LoQueNoVale_NoTocaNada(string origen, string destino, int cantidad, string producto)
        {
            IRepositorioCambioHuecoReposicion repositorio = A.Fake<IRepositorioCambioHuecoReposicion>();

            ResultadoCambioHueco resultado = await new ServicioCambioHuecoReposicion(repositorio)
                .Cambiar("1", TRASPASO, Cambio(origen, destino, cantidad, producto), "Andrey");

            Assert.AreEqual(EstadoCambioHueco.NoValido, resultado.Estado);
            A.CallTo(repositorio).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cambiar_Ensayo_LlegaAlRepositorioYNoPisaSuMensaje()
        {
            IRepositorioCambioHuecoReposicion repositorio = A.Fake<IRepositorioCambioHuecoReposicion>();
            A.CallTo(() => repositorio.Cambiar("1", TRASPASO, "10342", "001004003", "003001002", 2, "Carlos", true))
                .Returns(new ResultadoCambioHueco { Estado = EstadoCambioHueco.Cambiado, Movidas = 2, Ensayo = true, Mensaje = "ENSAYO (no se ha guardado nada): …" });

            ResultadoCambioHueco resultado = await new ServicioCambioHuecoReposicion(repositorio).Cambiar("1", TRASPASO, Cambio(), "Carlos", ensayo: true);

            Assert.IsTrue(resultado.Ensayo);
            StringAssert.StartsWith(resultado.Mensaje, "ENSAYO");
        }

        [TestMethod]
        public async Task LeerAlternativas_SinElHuecoDondeNoEstabaYEnElOrdenDelRecorrido()
        {
            IRepositorioCambioHuecoReposicion repositorio = A.Fake<IRepositorioCambioHuecoReposicion>();
            A.CallTo(() => repositorio.LeerAlternativas("1", TRASPASO, "10342", "001004003")).Returns(new List<HuecoAlternativoDTO>
            {
                new HuecoAlternativoDTO { Ubicacion = "007/001/001", Codigo = "007001001", Cantidad = 1 },
                new HuecoAlternativoDTO { Ubicacion = "003/001/002", Codigo = "003001002", Cantidad = 6 }
            });

            List<HuecoAlternativoDTO> alternativas = await new ServicioCambioHuecoReposicion(repositorio).LeerAlternativas("1", TRASPASO, " 10342 ", "001/004/003");

            CollectionAssert.AreEqual(new[] { "003001002", "007001001" }, alternativas.Select(a => a.Codigo).ToArray(), "Como el picking: pasillo, columna, fila");
        }

        [TestMethod]
        public async Task LeerAlternativas_SinProducto_ListaVaciaSinPreguntar()
        {
            IRepositorioCambioHuecoReposicion repositorio = A.Fake<IRepositorioCambioHuecoReposicion>();

            Assert.AreEqual(0, (await new ServicioCambioHuecoReposicion(repositorio).LeerAlternativas("1", TRASPASO, " ", "001004003")).Count);
            A.CallTo(repositorio).MustNotHaveHappened();
        }

        // ---------------------------------------------------------------- Las rutas por tipo

        private static AlmacenController Controlador(IServicioCambioHuecoPicking picking = null, IServicioCambioHuecoReposicion reposicion = null,
            params string[] roles)
        {
            return new AlmacenController(A.Fake<IServicioPreparacionAlmacen>(), A.Fake<IServicioUbicacionesAlmacen>(),
                A.Fake<IServicioRecepcionCompras>(), A.Fake<IServicioRecepcionReposiciones>(), null, null, picking,
                cambioHuecoReposicion: reposicion)
            {
                User = new GenericPrincipal(new GenericIdentity("Andrey", "Bearer"), roles),
                Request = new HttpRequestMessage()
            };
        }

        [TestMethod]
        public async Task PostRecogidaRepo_Cambiado_200ConLasUnidadesYElUsuarioDelToken()
        {
            IServicioCambioHuecoReposicion servicio = A.Fake<IServicioCambioHuecoReposicion>();
            CambiarHuecoPickingDTO cambio = Cambio();
            A.CallTo(() => servicio.Cambiar("1", TRASPASO, cambio, "Andrey", false))
                .Returns(new ResultadoCambioHueco { Estado = EstadoCambioHueco.Cambiado, Movidas = 2, Mensaje = "2 unidades se cogen…" });

            IHttpActionResult respuesta = await Controlador(reposicion: servicio).PostCambiarHuecoDeLaRecogida("repo", TRASPASO, cambio);

            ResultadoCambiarHuecoDTO dto = ((OkNegotiatedContentResult<ResultadoCambiarHuecoDTO>)respuesta).Content;
            Assert.AreEqual(2, dto.Movidas);
            Assert.IsNull(dto.Ensayo, "Sin ensayo no viaja nada del ensayo");
        }

        [TestMethod]
        public async Task PostRecogidaRepo_SinStockLibreEnDestinoOTerminada_409ConElMotivo()
        {
            IServicioCambioHuecoReposicion servicio = A.Fake<IServicioCambioHuecoReposicion>();
            A.CallTo(() => servicio.Cambiar(A<string>._, A<int>._, A<CambiarHuecoPickingDTO>._, A<string>._, A<bool>._))
                .Returns(new ResultadoCambioHueco { Estado = EstadoCambioHueco.NoSePuede, Mensaje = "En el hueco de donde se va a coger solo hay 1 libres, no 2. No se ha cambiado nada." });

            var respuesta = (NegotiatedContentResult<string>)await Controlador(reposicion: servicio).PostCambiarHuecoDeLaRecogida("REPO", TRASPASO, Cambio());

            Assert.AreEqual(HttpStatusCode.Conflict, respuesta.StatusCode);
            StringAssert.Contains(respuesta.Content, "solo hay 1 libres");
        }

        [TestMethod]
        public async Task PostRecogida_EnsayoSinSerAdminNiDireccion_403SinTocarNada()
        {
            IServicioCambioHuecoReposicion servicio = A.Fake<IServicioCambioHuecoReposicion>();

            var respuesta = (NegotiatedContentResult<string>)await Controlador(reposicion: servicio).PostCambiarHuecoDeLaRecogida("REPO", TRASPASO, Cambio(), ensayo: true);

            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.StatusCode);
            A.CallTo(servicio).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostRecogida_EnsayoDeAdmin_200ConLasFilasAunqueNoSePueda()
        {
            IServicioCambioHuecoReposicion servicio = A.Fake<IServicioCambioHuecoReposicion>();
            var antes = new List<FilaEnsayoDTO> { new FilaEnsayoDTO { Tabla = "Ubicaciones", Clave = "1", Datos = "Producto=10342; Estado=4; Cantidad=3;" } };
            A.CallTo(() => servicio.Cambiar("1", TRASPASO, A<CambiarHuecoPickingDTO>._, "Andrey", true))
                .Returns(new ResultadoCambioHueco { Estado = EstadoCambioHueco.NoSePuede, Ensayo = true, Mensaje = "ENSAYO: …", FilasAntes = antes });

            IHttpActionResult respuesta = await Controlador(null, servicio, "Admin").PostCambiarHuecoDeLaRecogida("REPO", TRASPASO, Cambio(), ensayo: true);

            ResultadoCambiarHuecoDTO dto = ((OkNegotiatedContentResult<ResultadoCambiarHuecoDTO>)respuesta).Content;
            Assert.AreEqual(true, dto.Ensayo);
            Assert.AreSame(antes, dto.FilasAntes);
        }

        [TestMethod]
        public async Task PostRecogida_UnTipoQueNoLoAdmite_400()
        {
            Assert.IsInstanceOfType(await Controlador(A.Fake<IServicioCambioHuecoPicking>(), A.Fake<IServicioCambioHuecoReposicion>())
                .PostCambiarHuecoDeLaRecogida("DEVP", 5, Cambio()), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task GetAlternativasRecogida_Repo_LasDelServicioDeReposicion()
        {
            IServicioCambioHuecoReposicion servicio = A.Fake<IServicioCambioHuecoReposicion>();
            var otros = new List<HuecoAlternativoDTO> { new HuecoAlternativoDTO { Ubicacion = "003/001/002", Codigo = "003001002", Cantidad = 6 } };
            A.CallTo(() => servicio.LeerAlternativas("1", TRASPASO, "10342", "001004003")).Returns(otros);

            IHttpActionResult respuesta = await Controlador(reposicion: servicio).GetAlternativasDeLaRecogida("REPO", TRASPASO, "10342", "001004003");

            Assert.AreSame(otros, ((OkNegotiatedContentResult<List<HuecoAlternativoDTO>>)respuesta).Content);
        }

        [TestMethod]
        public async Task GetAlternativasRecogida_UnTipoQueNoLoAdmite_ListaVacia()
        {
            IHttpActionResult respuesta = await Controlador(A.Fake<IServicioCambioHuecoPicking>(), A.Fake<IServicioCambioHuecoReposicion>())
                .GetAlternativasDeLaRecogida("DEVP", 5, "10342", "001004003");

            Assert.AreEqual(0, ((OkNegotiatedContentResult<List<HuecoAlternativoDTO>>)respuesta).Content.Count);
        }

        // ---- PICK por la ruta por tipo: lo mismo que la ruta de siempre del picking (Ariadna pide primero la de tipo)

        [TestMethod]
        public async Task GetAlternativasRecogida_Pick_LoMismoQuePickingAlternativas()
        {
            IServicioCambioHuecoPicking picking = A.Fake<IServicioCambioHuecoPicking>();
            var otros = new List<HuecoAlternativoDTO> { new HuecoAlternativoDTO { Ubicacion = "003/001/002", Codigo = "003001002", Cantidad = 6 } };
            A.CallTo(() => picking.LeerAlternativas("1", 99633, "22624", "001007002")).Returns(otros);
            AlmacenController controlador = Controlador(picking, A.Fake<IServicioCambioHuecoReposicion>());

            IHttpActionResult porTipo = await controlador.GetAlternativasDeLaRecogida("PICK", 99633, "22624", "001007002");
            IHttpActionResult deSiempre = await controlador.GetAlternativasDelPicking(99633, "22624", "001007002");

            Assert.AreSame(otros, ((OkNegotiatedContentResult<List<HuecoAlternativoDTO>>)porTipo).Content);
            Assert.AreSame(otros, ((OkNegotiatedContentResult<List<HuecoAlternativoDTO>>)deSiempre).Content);
        }

        [TestMethod]
        public async Task PostRecogidaPick_LoMismoQuePickingCambiarHueco()
        {
            IServicioCambioHuecoPicking picking = A.Fake<IServicioCambioHuecoPicking>();
            IServicioCambioHuecoReposicion reposicion = A.Fake<IServicioCambioHuecoReposicion>();
            CambiarHuecoPickingDTO cambio = Cambio("001007002", "003001002", 2, "22624");
            A.CallTo(() => picking.Cambiar("1", 99633, cambio, "Andrey"))
                .Returns(new ResultadoCambioHueco { Estado = EstadoCambioHueco.Cambiado, Movidas = 1, Mensaje = "Solo había 1 libres en ese hueco" });
            AlmacenController controlador = Controlador(picking, reposicion);

            var porTipo = (OkNegotiatedContentResult<ResultadoCambiarHuecoDTO>)await controlador.PostCambiarHuecoDeLaRecogida("pick", 99633, cambio);
            var deSiempre = (OkNegotiatedContentResult<ResultadoCambiarHuecoDTO>)await controlador.PostCambiarHueco(99633, cambio);

            Assert.AreEqual(deSiempre.Content.Movidas, porTipo.Content.Movidas);
            Assert.AreEqual(deSiempre.Content.Mensaje, porTipo.Content.Mensaje);
            A.CallTo(() => picking.Cambiar("1", 99633, cambio, "Andrey")).MustHaveHappenedTwiceExactly();
            A.CallTo(reposicion).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostPicking_EnsayoDeDireccion_PasaPorElEnsayoDelPicking()
        {
            IServicioCambioHuecoPicking picking = A.Fake<IServicioCambioHuecoPicking>();
            A.CallTo(() => picking.Ensayar("1", 99633, A<CambiarHuecoPickingDTO>._, "Andrey"))
                .Returns(new ResultadoCambioHueco { Estado = EstadoCambioHueco.Cambiado, Ensayo = true, Movidas = 2, Mensaje = "ENSAYO (no se ha guardado nada): …" });

            IHttpActionResult respuesta = await Controlador(picking, null, NestoAPI.Models.Constantes.GruposSeguridad.DIRECCION)
                .PostCambiarHueco(99633, Cambio("001007002", "003001002", 2, "22624"), ensayo: true);

            Assert.AreEqual(true, ((OkNegotiatedContentResult<ResultadoCambiarHuecoDTO>)respuesta).Content.Ensayo);
            A.CallTo(() => picking.Cambiar(A<string>._, A<int>._, A<CambiarHuecoPickingDTO>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void RecogidaAlmacenDTO_ElAlmacenViajaALaApp()
        {
            // Ariadna#12: la app no supone Algete
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(new RecogidaAlmacenDTO { Tipo = "REPO", Numero = TRASPASO, Almacen = "ALG" });

            StringAssert.Contains(json, "\"Almacen\":\"ALG\"");
        }

        // ---------------------------------------------------------------- El cuadre del ensayo

        [TestMethod]
        public void FotoCambioHueco_Cuadre_SumaLasCantidadesEnValorAbsoluto()
        {
            var antes = new List<FilaEnsayoDTO>
            {
                new FilaEnsayoDTO { Datos = "Producto=10342; Estado=4; Cantidad=3; Hueco=001/004/003" },
                new FilaEnsayoDTO { Datos = "Producto=10342; Estado=0; Cantidad=8; Hueco=003/001/002" }
            };
            var despues = new List<FilaEnsayoDTO>
            {
                new FilaEnsayoDTO { Datos = "Producto=10342; Estado=4; Cantidad=1; Hueco=001/004/003" },
                new FilaEnsayoDTO { Datos = "Producto=10342; Estado=0; Cantidad=6; Hueco=003/001/002" },
                new FilaEnsayoDTO { Datos = "Producto=10342; Estado=4; Cantidad=2; Hueco=003/001/002" },
                new FilaEnsayoDTO { Datos = "Producto=10342; Estado=2; Cantidad=2; Hueco=-" }
            };

            Assert.AreEqual("se cambiarían 2. Unidades en esas filas: 11 antes y 11 después (cuadra).", FotoCambioHueco.Cuadre(antes, despues, 2));
        }
    }
}
