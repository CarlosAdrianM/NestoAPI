using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#546 (28/09/26): pendientes de GLS guardados con el servicio 48 / horario 0 de CTT
    /// (249165, 249181, 249182). El catálogo de servicios/horarios/retornos de cada agencia vive en su
    /// perfil y la API rechaza lo que no es de la agencia.
    /// </summary>
    [TestClass]
    public class CatalogoServiciosAgenciasTests
    {
        private const int GLS = Constantes.Agencias.AGENCIA_GLS;
        private const int CTT = Constantes.Agencias.AGENCIA_CTT;
        private const int INNOVATRANS = Constantes.Agencias.AGENCIA_INNOVATRANS;
        private const int CANTERAS = Constantes.Agencias.AGENCIA_CANTERAS;
        private const int CEX = Constantes.Agencias.AGENCIA_CORREOS_EXPRESS;

        [TestMethod]
        public void GLS_ServicioYHorarioDeCTT_SeRechazaConMensajeClaro()
        {
            string error = CatalogoServiciosAgencias.Validar(GLS, 48, 0, 0);

            Assert.IsNotNull(error);
            StringAssert.Contains(error, "GLS");
            StringAssert.Contains(error, "servicio 48");
            StringAssert.Contains(error, "horario 0");
            StringAssert.Contains(error, "96 BusinessParcel", "El mensaje dice cuáles son los válidos");
        }

        [TestMethod]
        public void GLS_LoDeSiempre_Pasa()
        {
            Assert.IsNull(CatalogoServiciosAgencias.Validar(GLS, 96, 18, 0));
            Assert.IsNull(CatalogoServiciosAgencias.Validar(GLS, 96, 18, 1));
            Assert.IsNull(CatalogoServiciosAgencias.Validar(GLS, 96, 18, 2));
            Assert.IsNull(CatalogoServiciosAgencias.Validar(GLS, 96, 10, 0));
            Assert.IsNull(CatalogoServiciosAgencias.Validar(GLS, 6, 10, 0));
        }

        [TestMethod]
        public void GLS_SoloElRetornoMal_SeRechaza()
        {
            string error = CatalogoServiciosAgencias.Validar(GLS, 96, 18, 3);

            StringAssert.Contains(error, "retorno 3");
            Assert.IsFalse(error.Contains("servicio 96"), "Solo se nombra lo que está mal");
        }

        [TestMethod]
        public void CTT_ServicioCeroSinElegir_SeSigueAdmitiendo()
        {
            // ~200 envíos desde agosto con servicio 0: MapeadorTipoServicioCTT lo trata como 48 h.
            Assert.IsNull(CatalogoServiciosAgencias.Validar(CTT, 0, 0, 0));
        }

        [TestMethod]
        public void CTT_SusServiciosYRetornos_Pasan()
        {
            Assert.IsNull(CatalogoServiciosAgencias.Validar(CTT, 48, 0, 0));
            Assert.IsNull(CatalogoServiciosAgencias.Validar(CTT, 24, 0, 1));
            Assert.IsNull(CatalogoServiciosAgencias.Validar(CTT, 48, 0, 2));
        }

        [TestMethod]
        public void CTT_ServicioDeGLS_SeRechaza()
        {
            Assert.IsNotNull(CatalogoServiciosAgencias.Validar(CTT, 96, 18, 0));
        }

        [TestMethod]
        public void Innovatrans_SinElegirPasa_PeroConRetornoNo()
        {
            Assert.IsNull(CatalogoServiciosAgencias.Validar(INNOVATRANS, 0, 0, 0));
            // 28/09/26: DataTrans no recibe el retorno; con retorno la recogida se perdería en silencio.
            Assert.IsNotNull(CatalogoServiciosAgencias.Validar(INNOVATRANS, 0, 0, 1));
            Assert.IsNotNull(CatalogoServiciosAgencias.Validar(INNOVATRANS, 0, 0, 2));
            Assert.IsNotNull(CatalogoServiciosAgencias.Validar(INNOVATRANS, 96, 18, 0));
        }

        [TestMethod]
        public void Canteras_SoloSinServicioYSinRetorno()
        {
            Assert.IsNull(CatalogoServiciosAgencias.Validar(CANTERAS, 0, 0, 0));
            Assert.IsNotNull(CatalogoServiciosAgencias.Validar(CANTERAS, 0, 0, 1));
        }

        [TestMethod]
        public void AgenciasSinCatalogo_NoSeValidan()
        {
            // Históricas: Glovo (7), Sending (10), OnTime y cualquier otra sin perfil con catálogo.
            Assert.IsNull(CatalogoServiciosAgencias.Validar(Constantes.Agencias.AGENCIA_GLOVO, 99, 99, 9));
            Assert.IsNull(CatalogoServiciosAgencias.Validar(Constantes.Agencias.AGENCIA_SENDING, 1, 1, 1));
            Assert.IsNull(CatalogoServiciosAgencias.Validar(3, 55, 55, 5));
            Assert.IsNull(CatalogoServiciosAgencias.De(Constantes.Agencias.AGENCIA_SENDING));
        }

        [TestMethod]
        public void CombinacionesRealesDesde2026_Pasan()
        {
            // Consulta de control del 28/09/26 sobre EnviosAgencia (Fecha >= 2026). Quedan fuera a propósito
            // solo las que dejó el fallo de la respuesta tardía (GLS 48/0 y GLS 96/0) y un Innovatrans 96/18.
            short[][] reales =
            {
                new short[] { 1, 96, 10, 0 }, new short[] { 1, 96, 18, 0 }, new short[] { 1, 96, 18, 1 }, new short[] { 1, 96, 18, 2 },
                new short[] { 8, 63, 0, 0 }, new short[] { 8, 66, 0, 0 }, new short[] { 8, 92, 0, 0 },
                new short[] { 11, 0, 0, 0 }, new short[] { 12, 0, 0, 0 },
                new short[] { 13, 0, 0, 0 }, new short[] { 13, 48, 0, 0 },
                new short[] { 10, 1, 1, 0 }, new short[] { 10, 1, 1, 1 }
            };
            foreach (short[] r in reales)
            {
                Assert.IsNull(CatalogoServiciosAgencias.Validar(r[0], r[1], r[2], r[3]), $"Agencia {r[0]} {r[1]}/{r[2]}/{r[3]}");
            }
        }

        [TestMethod]
        public void TarifasYDefaults_EstanEnElCatalogoDeSuAgencia()
        {
            // Si alguien añade una tarifa o cambia un default sin tocar el catálogo, el comparador o
            // CrearEtiquetaPendiente crearían envíos que la propia API rechazaría.
            foreach (ITarifaAgencia tarifa in new RegistroTarifas().Todas())
            {
                CatalogoServiciosAgencia catalogo = CatalogoServiciosAgencias.De(tarifa.AgenciaId);
                if (catalogo == null)
                {
                    continue;
                }
                Assert.IsTrue(catalogo.Servicios.ContainsKey(tarifa.ServicioId), $"{tarifa.GetType().Name}: servicio {tarifa.ServicioId}");
                Assert.IsTrue(catalogo.Horarios.ContainsKey(tarifa.HorarioDefectoId), $"{tarifa.GetType().Name}: horario {tarifa.HorarioDefectoId}");
            }

            RegistroAgencias registro = RegistroAgencias.PorReflexionSinPuerta();
            foreach (IPerfilConDefaultsEnvio perfil in registro.Perfiles.OfType<IPerfilConDefaultsEnvio>())
            {
                CatalogoServiciosAgencia catalogo = CatalogoServiciosAgencias.De(perfil.AgenciaId);
                if (catalogo == null)
                {
                    continue;
                }
                foreach (string cp in new[] { "28001", "07001", "1000-001", "75001" })
                {
                    var defaults = perfil.DefaultsEnvio(cp);
                    Assert.IsTrue(catalogo.Servicios.ContainsKey(defaults.Servicio), $"Agencia {perfil.AgenciaId}, CP {cp}: servicio {defaults.Servicio}");
                    Assert.IsTrue(catalogo.Horarios.ContainsKey(defaults.Horario), $"Agencia {perfil.AgenciaId}, CP {cp}: horario {defaults.Horario}");
                }
            }
        }

        [TestMethod]
        public void TodasLasAgenciasConCatalogo_TienenElRetornoCero()
        {
            // El «sin retorno» tiene que valer siempre: es el valor por defecto del envío.
            foreach (IPerfilConCatalogoServicios perfil in RegistroAgencias.PorReflexionSinPuerta().Perfiles.OfType<IPerfilConCatalogoServicios>())
            {
                Assert.IsTrue(perfil.CatalogoServicios.Retornos.ContainsKey(0), $"Agencia {perfil.AgenciaId}");
            }
        }

        // ---- Cuándo se valida un PUT ----

        private static EnviosAgencia Nuevo(int agencia, short servicio, short horario, short retorno)
            => new EnviosAgencia { Agencia = agencia, Servicio = servicio, Horario = horario, Retorno = retorno };

        [TestMethod]
        public void Modificacion_PendienteEnBD_SeValidaSiempre()
        {
            // El caso del 28/09: el pendiente es lo que se va a imprimir y tramitar.
            Assert.IsTrue(CatalogoServiciosAgencias.DebeValidarModificacion(
                (short)Constantes.Agencias.ESTADO_PENDIENTE, GLS, 48, 0, 0, Nuevo(GLS, 48, 0, 0)));
        }

        [TestMethod]
        public void Modificacion_TramitadoSinTocarServicio_NoSeValida()
        {
            // 249066/249117 (GLS 96/0, ya entregados): cambiarles el reembolso o la dirección no se bloquea.
            Assert.IsFalse(CatalogoServiciosAgencias.DebeValidarModificacion(
                Constantes.Agencias.ESTADO_TRAMITADO, GLS, 96, 0, 0, Nuevo(GLS, 96, 0, 0)));
        }

        [TestMethod]
        public void Modificacion_TramitadoCambiandoAgenciaServicioHorarioORetorno_SeValida()
        {
            short tramitado = Constantes.Agencias.ESTADO_TRAMITADO;
            Assert.IsTrue(CatalogoServiciosAgencias.DebeValidarModificacion(tramitado, GLS, 96, 18, 0, Nuevo(CTT, 96, 18, 0)));
            Assert.IsTrue(CatalogoServiciosAgencias.DebeValidarModificacion(tramitado, GLS, 96, 18, 0, Nuevo(GLS, 48, 18, 0)));
            Assert.IsTrue(CatalogoServiciosAgencias.DebeValidarModificacion(tramitado, GLS, 96, 18, 0, Nuevo(GLS, 96, 0, 0)));
            Assert.IsTrue(CatalogoServiciosAgencias.DebeValidarModificacion(tramitado, GLS, 96, 18, 0, Nuevo(GLS, 96, 18, 1)));
        }

        [TestMethod]
        public void CEX_ServiciosDeSuLista_Pasan()
        {
            Assert.IsNull(CatalogoServiciosAgencias.Validar(CEX, 92, 0, 0));
            Assert.IsNull(CatalogoServiciosAgencias.Validar(CEX, 90, 0, 0));
            Assert.IsNotNull(CatalogoServiciosAgencias.Validar(CEX, 96, 18, 0));
        }
    }
}
