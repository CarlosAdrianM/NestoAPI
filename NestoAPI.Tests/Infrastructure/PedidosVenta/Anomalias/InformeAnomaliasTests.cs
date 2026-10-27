using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.PedidosVenta.Anomalias;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta.Anomalias
{
    /// <summary>NestoAPI#715: la agregación del informe y GET api/PedidosVenta/Anomalias/Informe.</summary>
    [TestClass]
    public class InformeAnomaliasTests
    {
        private static readonly DateTime DIA = new DateTime(2026, 10, 20, 9, 0, 0);

        private static FilaAnomaliaPedido Fila(int pedido, string regla, string resultado = null, string aplicacion = "Nesto", DateTime? fecha = null, decimal? puntuacion = null)
        {
            return new FilaAnomaliaPedido
            {
                Fecha = fecha ?? DIA.AddMinutes(pedido % 100),
                Empresa = "1",
                Pedido = pedido,
                Regla = regla,
                Resultado = resultado,
                Aplicacion = aplicacion,
                Puntuacion = puntuacion
            };
        }

        private static List<FilaAnomaliaPedido> Filas()
        {
            return new List<FilaAnomaliaPedido>
            {
                // Pedido 1: Nesto, cantidad alta corregida
                Fila(1, ReglasAnomalia.CONTEXTO, ResultadosAnomalia.CORREGIDO_48H),
                Fila(1, ReglasAnomalia.CANTIDAD_ALTA, ResultadosAnomalia.CORREGIDO_48H, puntuacion: 3m),
                Fila(1, ReglasAnomalia.CANTIDAD_VS_MAXIMO, ResultadosAnomalia.CORREGIDO_48H, puntuacion: 1.5m),
                // Pedido 2: Nesto, cantidad alta sin cambios
                Fila(2, ReglasAnomalia.CONTEXTO, ResultadosAnomalia.SIN_CAMBIOS),
                Fila(2, ReglasAnomalia.CANTIDAD_ALTA, ResultadosAnomalia.SIN_CAMBIOS, puntuacion: 1m),
                // Pedido 3: NestoApp, precio bajo abonado
                Fila(3, ReglasAnomalia.CONTEXTO, ResultadosAnomalia.SIN_CAMBIOS, "NestoApp"),
                Fila(3, ReglasAnomalia.PRECIO_BAJO, ResultadosAnomalia.ABONADO, "NestoApp"),
                // Pedido 4: NestoApp, sin señales, pendiente de etiquetar; solo falta de stock
                Fila(4, ReglasAnomalia.CONTEXTO, null, "NestoApp"),
                Fila(4, ReglasAnomalia.SIN_STOCK, null, "NestoApp"),
                // Pedido 5: TiendasNuevaVision, pendiente, cantidad alta pendiente
                Fila(5, ReglasAnomalia.CONTEXTO, null, "TiendasNuevaVision"),
                Fila(5, ReglasAnomalia.CANTIDAD_ALTA, null, "TiendasNuevaVision", puntuacion: 2m),
                // Pedido 1 otra vez (PUT), sin señales
                Fila(1, ReglasAnomalia.CONTEXTO, ResultadosAnomalia.SIN_CAMBIOS, fecha: DIA.AddHours(2))
            };
        }

        [TestMethod]
        public void Informe_Recuentos()
        {
            InformeAnomaliasDTO informe = InformeAnomalias.Calcular(Filas(), DIA.Date, DIA.Date.AddDays(1));

            Assert.AreEqual(6, informe.Grabaciones);
            Assert.AreEqual(5, informe.Pedidos);
            Assert.AreEqual(4, informe.PedidosConSenal, "1, 2, 3 y 5; el 4 solo tiene SinStock, que no cuenta como señal");
            Assert.AreEqual(6, informe.Senales);
            Assert.AreEqual(4, informe.GrabacionesEtiquetadas);
            Assert.AreEqual(0.25m, informe.TasaBaseCorreccion, "1 de 4 grabaciones etiquetadas se corrigió");
        }

        [TestMethod]
        public void Informe_PrecisionPorRegla()
        {
            InformeAnomaliasDTO informe = InformeAnomalias.Calcular(Filas(), DIA.Date, DIA.Date.AddDays(1));

            InformeReglaAnomaliaDTO cantidad = informe.Reglas.Single(r => r.Regla == ReglasAnomalia.CANTIDAD_ALTA);
            Assert.AreEqual(3, cantidad.Senales);
            Assert.AreEqual(3, cantidad.Pedidos);
            Assert.AreEqual(2, cantidad.Etiquetadas);
            Assert.AreEqual(1, cantidad.CorregidasEn48h);
            Assert.AreEqual(0, cantidad.Abonadas);
            Assert.AreEqual(1, cantidad.SinCambios);
            Assert.AreEqual(0.5m, cantidad.Precision);
            Assert.AreEqual(2m, cantidad.PuntuacionMedia);

            InformeReglaAnomaliaDTO precio = informe.Reglas.Single(r => r.Regla == ReglasAnomalia.PRECIO_BAJO);
            Assert.AreEqual(1m, precio.Precision, "abonado cuenta como acierto");
            Assert.IsNull(precio.PuntuacionMedia);

            InformeReglaAnomaliaDTO stock = informe.Reglas.Single(r => r.Regla == ReglasAnomalia.SIN_STOCK);
            Assert.IsNull(stock.Precision, "sin etiquetadas no hay precisión");
            Assert.AreEqual(ReglasAnomalia.CANTIDAD_ALTA, informe.Reglas[0].Regla, "ordenadas por número de señales");
            Assert.IsFalse(informe.Reglas.Any(r => r.Regla == ReglasAnomalia.CONTEXTO));
        }

        [TestMethod]
        public void Informe_PorAplicacion()
        {
            InformeAnomaliasDTO informe = InformeAnomalias.Calcular(Filas(), DIA.Date, DIA.Date.AddDays(1));

            InformeAplicacionAnomaliaDTO nesto = informe.Aplicaciones.Single(a => a.Aplicacion == "Nesto");
            Assert.AreEqual(3, nesto.Grabaciones);
            Assert.AreEqual(2, nesto.GrabacionesConSenal, "el PUT del pedido 1 no tuvo señales");
            Assert.AreEqual(3, nesto.Senales);
            InformeAplicacionAnomaliaDTO app = informe.Aplicaciones.Single(a => a.Aplicacion == "NestoApp");
            Assert.AreEqual(2, app.Grabaciones);
            Assert.AreEqual(1, app.GrabacionesConSenal);
            Assert.AreEqual(1, informe.Aplicaciones.Single(a => a.Aplicacion == "TiendasNuevaVision").GrabacionesConSenal);
        }

        [TestMethod]
        public void Informe_SinFilas_Vacio()
        {
            InformeAnomaliasDTO informe = InformeAnomalias.Calcular(null, DIA.Date, DIA.Date.AddDays(1));

            Assert.AreEqual(0, informe.Grabaciones);
            Assert.AreEqual(0, informe.Reglas.Count);
            Assert.IsNull(informe.TasaBaseCorreccion);
        }

        private static ClaimsPrincipal Usuario(params string[] roles)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\Carlos") };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, "NUEVAVISION\\" + r)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private static PedidosVentaAnomaliasController Controller(RepositorioAnomaliasEnMemoria repositorio, ClaimsPrincipal usuario)
        {
            return new PedidosVentaAnomaliasController(repositorio)
            {
                User = usuario,
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
        }

        [TestMethod]
        public void Controller_Informe_DireccionOInformatica()
        {
            var repositorio = new RepositorioAnomaliasEnMemoria();
            repositorio.Insertar(Filas());
            try
            {
                InterruptorAnomalias.LeerAjuste = clave => "Sombra";

                var resultado = Controller(repositorio, Usuario(Constantes.GruposSeguridad.DIRECCION)).GetInforme(DIA.Date, DIA.Date.AddDays(1)) as OkNegotiatedContentResult<InformeAnomaliasDTO>;

                Assert.IsNotNull(resultado);
                Assert.AreEqual(6, resultado.Content.Grabaciones);
                Assert.AreEqual("Sombra", resultado.Content.Modo);
                Assert.IsTrue(resultado.Content.TablaExiste);
                Assert.IsNotNull(Controller(repositorio, Usuario(Constantes.GruposSeguridad.INFORMATICA)).GetInforme() as OkNegotiatedContentResult<InformeAnomaliasDTO>);
            }
            finally
            {
                InterruptorAnomalias.LeerAjuste = null;
            }
        }

        [TestMethod]
        public void Controller_Informe_OtrosGrupos_403()
        {
            var repositorio = new RepositorioAnomaliasEnMemoria();

            IHttpActionResult resultado = Controller(repositorio, Usuario(Constantes.GruposSeguridad.ALMACEN, "Vendedores")).GetInforme();

            var respuesta = resultado as ResponseMessageResult;
            Assert.IsNotNull(respuesta);
            Assert.AreEqual(HttpStatusCode.Forbidden, respuesta.Response.StatusCode);
            Assert.IsFalse(PedidosVentaAnomaliasController.PuedeVer(null));
        }

        [TestMethod]
        public void Controller_Informe_SinTabla_InformeVacioConAviso()
        {
            var repositorio = new RepositorioAnomaliasEnMemoria { Existe = false };

            var resultado = Controller(repositorio, Usuario(Constantes.GruposSeguridad.DIRECCION)).GetInforme() as OkNegotiatedContentResult<InformeAnomaliasDTO>;

            Assert.IsNotNull(resultado);
            Assert.IsFalse(resultado.Content.TablaExiste);
            Assert.AreEqual(0, resultado.Content.Grabaciones);
        }

        [TestMethod]
        public void Controller_Informe_PorDefectoUltimos30Dias()
        {
            var repositorio = new RepositorioAnomaliasEnMemoria();
            repositorio.Insertar(new List<FilaAnomaliaPedido>
            {
                Fila(1, ReglasAnomalia.CONTEXTO, fecha: DateTime.Today.AddDays(-1)),
                Fila(2, ReglasAnomalia.CONTEXTO, fecha: DateTime.Today.AddDays(-40))
            });

            var resultado = Controller(repositorio, Usuario(Constantes.GruposSeguridad.DIRECCION)).GetInforme() as OkNegotiatedContentResult<InformeAnomaliasDTO>;

            Assert.AreEqual(1, resultado.Content.Grabaciones);
            Assert.AreEqual(DateTime.Today.AddDays(1), resultado.Content.Hasta);
            Assert.AreEqual(DateTime.Today.AddDays(1 - InformeAnomalias.DIAS_POR_DEFECTO), resultado.Content.Desde);
        }

        [TestMethod]
        public void Controller_Informe_FechasAlReves_400()
        {
            IHttpActionResult resultado = Controller(new RepositorioAnomaliasEnMemoria(), Usuario(Constantes.GruposSeguridad.DIRECCION)).GetInforme(DIA, DIA.AddDays(-1));

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }
    }
}
