using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Models.OfertasCombinadas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.OfertasAutorizadas
{
    public interface IServicioOfertasAutorizadas
    {
        /// <summary>Ofertas vigentes hoy de las tres pestañas, para la pantalla de solo lectura de NestoApp.</summary>
        Task<OfertasAutorizadasDTO> LeerVigentes(string empresa);

        /// <summary>
        /// Manda a todos los vendedores de NestoApp una push que resume la oferta. Solo se llama cuando
        /// quien la ha guardado en Nesto dice que sí a informarles: guardar una oferta NO avisa a nadie.
        /// </summary>
        Task<ResultadoInformarVendedoresDTO> InformarVendedores(string tipo, int id, bool esNueva);

        /// <summary>¿Se conoce este tipo de oferta? (combinada, familia, escalonada)</summary>
        bool EsTipoValido(string tipo);
    }

    /// <summary>La oferta no existe (el controller lo traduce a 404).</summary>
    public class OfertaAutorizadaNoEncontradaException : Exception
    {
        public OfertaAutorizadaNoEncontradaException(string mensaje) : base(mensaje) { }
    }

    /// <summary>La oferta existe pero no es algo de lo que avisar (el controller lo traduce a 400).</summary>
    public class OfertaNoAvisableException : Exception
    {
        public OfertaNoAvisableException(string mensaje) : base(mensaje) { }
    }

    /// <summary>
    /// NestoAPI#233: núcleo común de las tres pestañas del mantenimiento de ofertas. Decisión de Carlos
    /// (29/09/26): la push a los vendedores NO sale sola al crear o modificar; Nesto pregunta tras
    /// guardar «¿Desea informar a los vendedores…?» y solo si dicen que sí llama aquí.
    /// </summary>
    public class ServicioOfertasAutorizadas : IServicioOfertasAutorizadas, IDisposable
    {
        internal const string TIPO_NOTIFICACION = "OfertaAutorizada";
        internal const string RUTA_NESTOAPP = "/ofertas-autorizadas";
        internal const string TITULO_NUEVA = "Nueva oferta autorizada";
        internal const string TITULO_ACTUALIZADA = "Oferta actualizada";

        private readonly NVEntities db;
        private readonly bool esDuenoDelContexto;
        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Dictionary<string, ITipoOfertaAutorizada> tipos;

        /// <summary>Hoy, sustituible en tests.</summary>
        internal Func<DateTime> Hoy { get; set; } = () => DateTime.Today;

        public ServicioOfertasAutorizadas(IServicioNotificacionesPush notificaciones)
            : this(new NVEntities(), notificaciones, true) { }

        internal ServicioOfertasAutorizadas(NVEntities db, IServicioNotificacionesPush notificaciones)
            : this(db, notificaciones, false) { }

        private ServicioOfertasAutorizadas(NVEntities db, IServicioNotificacionesPush notificaciones, bool esDuenoDelContexto)
        {
            this.db = db;
            this.notificaciones = notificaciones;
            this.esDuenoDelContexto = esDuenoDelContexto;
            tipos = new ITipoOfertaAutorizada[]
            {
                new TipoOfertaCombinada(db),
                new TipoOfertaFamilia(db),
                new TipoOfertaEscalonada(db)
            }.ToDictionary(t => t.Tipo, StringComparer.OrdinalIgnoreCase);
        }

        public bool EsTipoValido(string tipo)
        {
            return tipo != null && tipos.ContainsKey(tipo);
        }

        public async Task<OfertasAutorizadasDTO> LeerVigentes(string empresa)
        {
            var resultado = new OfertasAutorizadasDTO();
            DateTime hoy = Hoy();
            foreach (ITipoOfertaAutorizada tipo in tipos.Values)
            {
                await tipo.AnadirVigentes(empresa, hoy, resultado).ConfigureAwait(false);
            }
            return resultado;
        }

        public async Task<ResultadoInformarVendedoresDTO> InformarVendedores(string tipo, int id, bool esNueva)
        {
            if (!EsTipoValido(tipo))
            {
                throw new ArgumentException($"Tipo de oferta desconocido: '{tipo}'. Debe ser {string.Join(", ", tipos.Keys)}");
            }
            ITipoOfertaAutorizada estrategia = tipos[tipo];

            AvisoOfertaAutorizada aviso = await estrategia.ConstruirAviso(id).ConfigureAwait(false);
            if (aviso == null)
            {
                throw new OfertaAutorizadaNoEncontradaException($"No existe la oferta {estrategia.Tipo} {id}");
            }
            if (aviso.MotivoNoSeAvisa != null)
            {
                throw new OfertaNoAvisableException(aviso.MotivoNoSeAvisa);
            }

            NotificacionPushDTO notificacion = ConstruirNotificacion(estrategia.Tipo, id, esNueva, aviso.Cuerpo);
            int enviados = await notificaciones
                .EnviarATodosDeAplicacion(Constantes.Aplicaciones.NESTO_APP, notificacion)
                .ConfigureAwait(false);

            return new ResultadoInformarVendedoresDTO
            {
                Titulo = notificacion.Titulo,
                Cuerpo = notificacion.Cuerpo,
                Ruta = notificacion.Datos["ruta"],
                DispositivosNotificados = enviados
            };
        }

        /// <summary>
        /// Contrato con NestoApp#137: la app navega a Datos["ruta"] al tocar la push; la pantalla lee
        /// tipo e id de la query para abrir esa pestaña y desplegar esa oferta. tipo e id van también
        /// sueltos en Datos por si la app los quiere sin parsear la ruta.
        /// </summary>
        internal static NotificacionPushDTO ConstruirNotificacion(string tipo, int id, bool esNueva, string cuerpo)
        {
            return new NotificacionPushDTO
            {
                Titulo = esNueva ? TITULO_NUEVA : TITULO_ACTUALIZADA,
                Cuerpo = cuerpo,
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string>
                {
                    { "ruta", $"{RUTA_NESTOAPP}?tipo={tipo}&id={id}" },
                    { "tipo", tipo },
                    { "id", id.ToString() }
                }
            };
        }

        public void Dispose()
        {
            if (esDuenoDelContexto)
            {
                db.Dispose();
            }
        }
    }
}
