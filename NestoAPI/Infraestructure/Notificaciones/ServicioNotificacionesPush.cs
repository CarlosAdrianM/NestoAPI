using Elmah;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Hosting;

namespace NestoAPI.Infraestructure.Notificaciones
{
    public class ServicioNotificacionesPush : IServicioNotificacionesPush
    {
        private static readonly object _lockInit = new object();
        private static bool _firebaseInitialized = false;
        private static readonly Dictionary<string, FirebaseApp> _firebaseApps = new Dictionary<string, FirebaseApp>();

        public static bool EstaInicializado => _firebaseInitialized;

        private static readonly Dictionary<string, string> _credencialesPorAplicacion = new Dictionary<string, string>
        {
            { Constantes.Aplicaciones.NESTO_APP, "firebase-adminsdk-nestoapp.json" },
            { Constantes.Aplicaciones.NESTO_TIENDAS, "firebase-adminsdk-nestotiendas.json" },
            // NestoAPI#575: Ariadna está en el proyecto Firebase de NestoTiendas (misma credencial, otra app Android)
            { Constantes.Aplicaciones.ARIADNA, "firebase-adminsdk-nestotiendas.json" }
        };

        private readonly Func<NVEntities> _crearContexto;

        public ServicioNotificacionesPush() : this(() => new NVEntities())
        {
        }

        /// <summary>NestoAPI#536: el aviso en tiempo real a Nesto (SignalR). Sustituible en tests.</summary>
        internal IAvisosTiempoReal AvisosTiempoReal { get; set; } = new AvisosTiempoReal();

        /// <summary>
        /// NestoAPI#575: manda una push SOLO de datos (tokens, datos, aplicación) y devuelve cuántas salieron. Sustituible
        /// en tests (Firebase no se inicializa en ellos).
        /// </summary>
        internal Func<IReadOnlyList<string>, Dictionary<string, string>, string, Task<int>> EnviadorDatos { get; set; }

        // Internal para tests (InternalsVisibleTo("NestoAPI.Tests")): permite inyectar un contexto
        // falso y comprobar que el buzón se guarda y se lee bien sin tocar la base de datos.
        internal ServicioNotificacionesPush(Func<NVEntities> crearContexto)
        {
            _crearContexto = crearContexto;
            EnviadorDatos = EnviarDatosConFirebase;
            InicializarFirebase();
        }

        private static void InicializarFirebase()
        {
            if (_firebaseInitialized)
            {
                return;
            }

            lock (_lockInit)
            {
                if (_firebaseInitialized)
                {
                    return;
                }

                bool alMenosUnoInicializado = false;

                foreach (var kvp in _credencialesPorAplicacion)
                {
                    try
                    {
                        string credentialPath = BuscarCredenciales(kvp.Value);
                        if (credentialPath != null)
                        {
                            var app = FirebaseApp.Create(new AppOptions
                            {
                                Credential = GoogleCredential.FromFile(credentialPath)
                            }, kvp.Key);
                            _firebaseApps[kvp.Key] = app;
                            alMenosUnoInicializado = true;
                        }
                        else
                        {
                            LogearEnElmah(new FileNotFoundException(
                                $"No se encontró credenciales Firebase para {kvp.Key}: {kvp.Value}"));
                        }
                    }
                    catch (Exception ex)
                    {
                        LogearEnElmah(new Exception(
                            $"Error inicializando Firebase para {kvp.Key}: {ex.Message}", ex));
                    }
                }

                _firebaseInitialized = alMenosUnoInicializado;
            }
        }

        private static string BuscarCredenciales(string nombreFichero)
        {
            string baseDir = HostingEnvironment.MapPath("~/Secrets");
            if (baseDir != null)
            {
                string ruta = Path.Combine(baseDir, nombreFichero);
                if (File.Exists(ruta))
                {
                    return ruta;
                }
            }

            return null;
        }

        private static FirebaseMessaging ObtenerMessaging(string aplicacion)
        {
            if (_firebaseApps.TryGetValue(aplicacion, out FirebaseApp app))
            {
                return FirebaseMessaging.GetMessaging(app);
            }

            // Fallback: intentar con la primera app disponible
            if (_firebaseApps.Count > 0)
            {
                return FirebaseMessaging.GetMessaging(_firebaseApps.Values.First());
            }

            return null;
        }

        public async Task<DispositivoNotificacion> RegistrarDispositivo(RegistrarDispositivoDTO registro, string usuario)
        {
            if (string.IsNullOrWhiteSpace(registro?.Token))
            {
                throw new ArgumentException("El token del dispositivo es obligatorio");
            }
            if (string.IsNullOrWhiteSpace(registro.Plataforma))
            {
                throw new ArgumentException("La plataforma es obligatoria");
            }
            if (string.IsNullOrWhiteSpace(registro.Aplicacion))
            {
                throw new ArgumentException("La aplicación es obligatoria");
            }

            using (NVEntities db = _crearContexto())
            {
                void ActualizarDatos(DispositivoNotificacion dispositivo)
                {
                    dispositivo.Usuario = usuario;
                    dispositivo.Empresa = registro.Empresa;
                    dispositivo.Vendedor = registro.Vendedor;
                    dispositivo.Cliente = registro.Cliente;
                    dispositivo.Contacto = registro.Contacto;
                    dispositivo.Plataforma = registro.Plataforma;
                    dispositivo.Aplicacion = registro.Aplicacion;
                    dispositivo.FechaUltimaActividad = DateTime.Now;
                    dispositivo.Activo = true;
                }

                // NestoAPI#575: en Ariadna el token es del aparato y lo comparten los mozos de la PDA (una fila por
                // mozo). En el resto, un token es de un solo usuario y el último en entrar se lo queda (como siempre).
                bool compartido = TokenCompartidoPorVariosUsuarios(registro.Aplicacion);
                System.Linq.Expressions.Expression<Func<DispositivoNotificacion, bool>> suyo = compartido
                    ? (d => d.Token == registro.Token && d.Usuario == usuario)
                    : (System.Linq.Expressions.Expression<Func<DispositivoNotificacion, bool>>)(d => d.Token == registro.Token);

                var existente = await db.DispositivosNotificaciones
                    .FirstOrDefaultAsync(suyo)
                    .ConfigureAwait(false);

                if (existente != null)
                {
                    ActualizarDatos(existente);
                    await db.SaveChangesAsync().ConfigureAwait(false);
                    return existente;
                }

                var nuevo = new DispositivoNotificacion
                {
                    Token = registro.Token,
                    FechaRegistro = DateTime.Now
                };
                ActualizarDatos(nuevo);

                db.DispositivosNotificaciones.Add(nuevo);
                try
                {
                    await db.SaveChangesAsync().ConfigureAwait(false);
                    return nuevo;
                }
                catch (System.Data.Entity.Infrastructure.DbUpdateException)
                {
                    // NestoAPI#389: carrera — dos registros simultáneos del mismo token (la app
                    // registra en el arranque y en el login casi a la vez) leen ambos "no existe"
                    // y el segundo insert viola UQ_DispositivosNotificaciones_Token. La fila ya
                    // existe (la creó la otra petición): se actualiza y en paz.
                    db.Entry(nuevo).State = System.Data.Entity.EntityState.Detached;
                    var ganador = await db.DispositivosNotificaciones
                        .FirstOrDefaultAsync(suyo)
                        .ConfigureAwait(false);
                    if (ganador == null && compartido)
                    {
                        // Sin Scripts/Issue575_DispositivosVariosUsuarios.sql la base de datos sigue con un usuario
                        // por token: el mozo se queda el token (como en NestoApp) y se avisa para lanzar el script.
                        ganador = await db.DispositivosNotificaciones
                            .FirstOrDefaultAsync(d => d.Token == registro.Token)
                            .ConfigureAwait(false);
                        LogearEnElmah(new Exception(
                            "[Push] Ariadna: el token de esta PDA ya era de otro mozo y la base de datos no deja compartirlo. " +
                            "Falta lanzar Scripts/Issue575_DispositivosVariosUsuarios.sql: hasta entonces, solo el último mozo recibe avisos."));
                    }
                    if (ganador == null)
                    {
                        throw; // no era la carrera del token duplicado
                    }
                    ActualizarDatos(ganador);
                    await db.SaveChangesAsync().ConfigureAwait(false);
                    return ganador;
                }
            }
        }

        public async Task<bool> DesregistrarDispositivo(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            using (NVEntities db = _crearContexto())
            {
                // NestoAPI#575: en una PDA de Ariadna el token tiene una fila por mozo. Si el token ya no vale
                // (Firebase dice Unregistered, o se desinstala la app), no vale para ninguno.
                List<DispositivoNotificacion> filas = await db.DispositivosNotificaciones
                    .Where(d => d.Token == token)
                    .ToListAsync()
                    .ConfigureAwait(false);

                if (filas.Count == 0)
                {
                    return false;
                }

                foreach (DispositivoNotificacion dispositivo in filas)
                {
                    dispositivo.Activo = false;
                }
                await db.SaveChangesAsync().ConfigureAwait(false);
                return true;
            }
        }

        public async Task<bool> DesregistrarDispositivoDeUsuario(string token, string usuario)
        {
            if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(usuario))
            {
                return false;
            }

            using (NVEntities db = _crearContexto())
            {
                List<DispositivoNotificacion> filas = await db.DispositivosNotificaciones
                    .Where(d => d.Token == token && d.Usuario == usuario)
                    .ToListAsync()
                    .ConfigureAwait(false);

                if (filas.Count == 0)
                {
                    return false;
                }

                foreach (DispositivoNotificacion dispositivo in filas)
                {
                    dispositivo.Activo = false;
                }
                await db.SaveChangesAsync().ConfigureAwait(false);
                return true;
            }
        }

        /// <summary>
        /// NestoAPI#575: la PDA de Ariadna la comparten varios mozos, así que su token puede estar a nombre de varios a la
        /// vez. En NestoApp y TNV el móvil es de una persona: un token, un usuario.
        /// </summary>
        internal static bool TokenCompartidoPorVariosUsuarios(string aplicacion)
            => string.Equals(aplicacion?.Trim(), Constantes.Aplicaciones.ARIADNA, StringComparison.OrdinalIgnoreCase);

        public async Task<List<DispositivoNotificacion>> ObtenerDispositivosUsuario(string usuario, string aplicacion)
        {
            using (NVEntities db = _crearContexto())
            {
                return await db.DispositivosNotificaciones
                    .Where(d => d.Usuario == usuario && d.Aplicacion == aplicacion && d.Activo)
                    .ToListAsync()
                    .ConfigureAwait(false);
            }
        }

        public async Task<List<DispositivoNotificacion>> ObtenerDispositivosVendedor(string empresa, string vendedor, string aplicacion)
        {
            using (NVEntities db = _crearContexto())
            {
                return await db.DispositivosNotificaciones
                    .Where(d => d.Empresa == empresa && d.Vendedor == vendedor && d.Aplicacion == aplicacion && d.Activo)
                    .ToListAsync()
                    .ConfigureAwait(false);
            }
        }

        public async Task<List<DispositivoNotificacion>> ObtenerDispositivosCliente(string empresa, string cliente, string aplicacion)
        {
            using (NVEntities db = _crearContexto())
            {
                return await db.DispositivosNotificaciones
                    .Where(d => d.Empresa == empresa && d.Cliente == cliente && d.Aplicacion == aplicacion && d.Activo)
                    .ToListAsync()
                    .ConfigureAwait(false);
            }
        }

        public async Task GuardarEnBuzonDeUsuario(string usuario, string aplicacion, NotificacionPushDTO notificacion)
        {
            if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(aplicacion) || notificacion == null)
            {
                return;
            }
            // Un destinatario "virtual" para reutilizar el guardado de siempre (una fila por usuario)
            await GuardarEnBuzon(new List<DispositivoNotificacion>
            {
                new DispositivoNotificacion { Usuario = usuario.Trim(), Aplicacion = aplicacion }
            }, notificacion, aplicacion).ConfigureAwait(false);

            // NestoAPI#575: a Ariadna, además, una push de DATOS que despierta la PDA. El buzón sigue siendo la verdad.
            if (TokenCompartidoPorVariosUsuarios(aplicacion))
            {
                await AvisarDispositivosDeAriadna(usuario.Trim(), notificacion).ConfigureAwait(false);
            }
        }

        private async Task AvisarDispositivosDeAriadna(string usuario, NotificacionPushDTO notificacion)
        {
            try
            {
                List<DispositivoNotificacion> dispositivos = await ObtenerDispositivosUsuario(usuario, Constantes.Aplicaciones.ARIADNA).ConfigureAwait(false);
                List<string> tokens = dispositivos.Select(d => d.Token).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().ToList();
                if (tokens.Count == 0)
                {
                    return;
                }
                _ = await EnviadorDatos(tokens, DatosPushAriadna(usuario, notificacion), Constantes.Aplicaciones.ARIADNA).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // La push es un extra: el aviso ya está en el buzón y la PDA lo verá al abrir o al volver a la app.
                LogearEnElmah(new Exception($"[Push] Ariadna: no se pudo avisar a {usuario}: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// NestoAPI#575: lo que lleva la push de Ariadna. Es SOLO de datos (sin el bloque de notificación) para que la PDA
        /// decida: si el mozo que está dentro es <c>usuarioDestino</c>, la enseña; si no, se la guarda a ese mozo.
        /// FCM no admite valores nulos, así que lo que falte va vacío.
        /// </summary>
        internal static Dictionary<string, string> DatosPushAriadna(string usuario, NotificacionPushDTO notificacion)
        {
            var datos = new Dictionary<string, string>();
            if (notificacion?.Datos != null)
            {
                foreach (KeyValuePair<string, string> par in notificacion.Datos)
                {
                    datos[par.Key] = par.Value ?? string.Empty;
                }
            }
            datos["usuarioDestino"] = usuario ?? string.Empty;
            datos["titulo"] = notificacion?.Titulo ?? string.Empty;
            datos["cuerpo"] = notificacion?.Cuerpo ?? string.Empty;
            if (!datos.ContainsKey("tipo") || string.IsNullOrEmpty(datos["tipo"]))
            {
                datos["tipo"] = notificacion?.Tipo ?? string.Empty;
            }
            return datos;
        }

        /// <summary>
        /// NestoAPI#575: push SOLO de datos (prioridad alta para que llegue con la PDA en reposo). Si Firebase dice que un
        /// token ya no existe, se apaga en todas sus filas.
        /// </summary>
        private async Task<int> EnviarDatosConFirebase(IReadOnlyList<string> tokens, Dictionary<string, string> datos, string aplicacion)
        {
            if (tokens == null || tokens.Count == 0 || !_firebaseInitialized)
            {
                return 0;
            }

            FirebaseMessaging messaging = ObtenerMessaging(aplicacion);
            if (messaging == null)
            {
                LogearEnElmah(new Exception($"[Push] No hay instancia Firebase para aplicación: {aplicacion}"));
                return 0;
            }

            var mensaje = new MulticastMessage
            {
                Tokens = tokens.ToList(),
                Data = datos,
                Android = new AndroidConfig { Priority = Priority.High }
            };
            BatchResponse respuesta = await messaging.SendEachForMulticastAsync(mensaje).ConfigureAwait(false);
            for (int i = 0; i < respuesta.Responses.Count; i++)
            {
                if (!respuesta.Responses[i].IsSuccess && respuesta.Responses[i].Exception?.MessagingErrorCode == MessagingErrorCode.Unregistered)
                {
                    _ = await DesregistrarDispositivo(tokens[i]).ConfigureAwait(false);
                }
            }
            return respuesta.SuccessCount;
        }

        public async Task<int> EnviarAUsuario(string usuario, string aplicacion, NotificacionPushDTO notificacion)
        {
            var dispositivos = await ObtenerDispositivosUsuario(usuario, aplicacion).ConfigureAwait(false);
            return await EnviarYGuardar(dispositivos, notificacion, aplicacion).ConfigureAwait(false);
        }

        public async Task<int> EnviarAVendedor(string empresa, string vendedor, NotificacionPushDTO notificacion)
        {
            string aplicacion = Constantes.Aplicaciones.NESTO_APP;
            var dispositivos = await ObtenerDispositivosVendedor(empresa, vendedor, aplicacion).ConfigureAwait(false);
            return await EnviarYGuardar(dispositivos, notificacion, aplicacion).ConfigureAwait(false);
        }

        public async Task<int> EnviarACliente(string empresa, string cliente, NotificacionPushDTO notificacion)
        {
            string aplicacion = Constantes.Aplicaciones.NESTO_TIENDAS;
            var dispositivos = await ObtenerDispositivosCliente(empresa, cliente, aplicacion).ConfigureAwait(false);
            return await EnviarYGuardar(dispositivos, notificacion, aplicacion).ConfigureAwait(false);
        }

        public async Task<int> EnviarATodosDeAplicacion(string aplicacion, NotificacionPushDTO notificacion)
        {
            List<DispositivoNotificacion> dispositivos;

            using (NVEntities db = _crearContexto())
            {
                dispositivos = await db.DispositivosNotificaciones
                    .Where(d => d.Aplicacion == aplicacion && d.Activo)
                    .ToListAsync()
                    .ConfigureAwait(false);
            }

            return await EnviarYGuardar(dispositivos, notificacion, aplicacion).ConfigureAwait(false);
        }

        /// <summary>
        /// Guarda SIEMPRE en el buzón antes de enviar: si FCM falla o el usuario descarta la
        /// notificación del sistema, el buzón es la red de seguridad (#387).
        /// </summary>
        private async Task<int> EnviarYGuardar(List<DispositivoNotificacion> dispositivos, NotificacionPushDTO notificacion, string aplicacion)
        {
            await GuardarEnBuzon(dispositivos, notificacion, aplicacion).ConfigureAwait(false);
            return await EnviarADispositivos(dispositivos, notificacion, aplicacion).ConfigureAwait(false);
        }

        /// <summary>
        /// Una fila por destinatario (no por dispositivo): si alguien tiene el móvil y la tablet,
        /// la notificación es una sola y su estado de leída también.
        /// </summary>
        private async Task GuardarEnBuzon(List<DispositivoNotificacion> dispositivos, NotificacionPushDTO notificacion, string aplicacion)
        {
            if (dispositivos == null || !dispositivos.Any() || notificacion == null)
            {
                return;
            }

            try
            {
                string datos = notificacion.Datos != null && notificacion.Datos.Any()
                    ? Newtonsoft.Json.JsonConvert.SerializeObject(notificacion.Datos)
                    : null;

                using (NVEntities db = _crearContexto())
                {
                    var destinatarios = dispositivos
                        .GroupBy(d => new { d.Usuario, d.Empresa, d.Vendedor, d.Cliente, d.Contacto })
                        .Select(g => g.Key);

                    foreach (var destinatario in destinatarios)
                    {
                        _ = db.NotificacionesBuzon.Add(new NotificacionBuzon
                        {
                            Usuario = destinatario.Usuario,
                            Empresa = destinatario.Empresa,
                            Vendedor = destinatario.Vendedor,
                            Cliente = destinatario.Cliente,
                            Contacto = destinatario.Contacto,
                            Aplicacion = aplicacion,
                            Titulo = notificacion.Titulo,
                            Cuerpo = notificacion.Cuerpo,
                            Datos = datos,
                            FechaCreacion = DateTime.Now
                        });
                    }

                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                }

                // NestoAPI#536: Nesto no recibe push; se le avisa por SignalR para que refresque la campana.
                if (aplicacion == Constantes.Aplicaciones.NESTO)
                {
                    AvisosTiempoReal.HayNotificacionesNuevas(dispositivos.Select(d => d.Usuario));
                }
            }
            catch (Exception ex)
            {
                // El buzón es un extra: si falla, la push tiene que salir igualmente.
                LogearEnElmah(new Exception($"[Buzón] No se pudo guardar la notificación '{notificacion.Titulo}': {ex.Message}", ex));
            }
        }

        public async Task<List<NotificacionBuzonDTO>> ObtenerBuzon(string usuario, string aplicacion, bool soloNoLeidas, int pagina, int tamanoPagina)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return new List<NotificacionBuzonDTO>();
            }

            if (pagina < 1)
            {
                pagina = 1;
            }

            if (tamanoPagina < 1 || tamanoPagina > 100)
            {
                tamanoPagina = 20;
            }

            using (NVEntities db = _crearContexto())
            {
                var consulta = ConsultaDelUsuario(db, usuario, aplicacion);

                if (soloNoLeidas)
                {
                    consulta = consulta.Where(n => n.FechaLeida == null);
                }

                List<NotificacionBuzon> notificaciones = await consulta
                    .OrderByDescending(n => n.FechaCreacion)
                    .ThenByDescending(n => n.Id)
                    .Skip((pagina - 1) * tamanoPagina)
                    .Take(tamanoPagina)
                    .ToListAsync()
                    .ConfigureAwait(false);

                return notificaciones.Select(AConvertirDTO).ToList();
            }
        }

        public async Task<int> ContarNoLeidas(string usuario, string aplicacion)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return 0;
            }

            using (NVEntities db = _crearContexto())
            {
                return await ConsultaDelUsuario(db, usuario, aplicacion)
                    .CountAsync(n => n.FechaLeida == null)
                    .ConfigureAwait(false);
            }
        }

        public async Task<bool> MarcarLeida(int id, string usuario)
        {
            using (NVEntities db = _crearContexto())
            {
                // El filtro por usuario NO es decorativo: sin él, cualquiera podría marcar por id
                // las notificaciones de otro.
                NotificacionBuzon notificacion = await db.NotificacionesBuzon
                    .FirstOrDefaultAsync(n => n.Id == id && n.Usuario == usuario && n.FechaEliminada == null)
                    .ConfigureAwait(false);

                if (notificacion == null)
                {
                    return false;
                }

                if (notificacion.FechaLeida == null)
                {
                    notificacion.FechaLeida = DateTime.Now;
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                }

                return true;
            }
        }

        public async Task<int> MarcarTodasLeidas(string usuario, string aplicacion)
        {
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return 0;
            }

            using (NVEntities db = _crearContexto())
            {
                List<NotificacionBuzon> pendientes = await ConsultaDelUsuario(db, usuario, aplicacion)
                    .Where(n => n.FechaLeida == null)
                    .ToListAsync()
                    .ConfigureAwait(false);

                if (!pendientes.Any())
                {
                    return 0;
                }

                DateTime ahora = DateTime.Now;
                foreach (NotificacionBuzon notificacion in pendientes)
                {
                    notificacion.FechaLeida = ahora;
                }

                _ = await db.SaveChangesAsync().ConfigureAwait(false);
                return pendientes.Count;
            }
        }

        public async Task<bool> EliminarDelBuzon(int id, string usuario)
        {
            using (NVEntities db = _crearContexto())
            {
                NotificacionBuzon notificacion = await db.NotificacionesBuzon
                    .FirstOrDefaultAsync(n => n.Id == id && n.Usuario == usuario && n.FechaEliminada == null)
                    .ConfigureAwait(false);

                if (notificacion == null)
                {
                    return false;
                }

                // Borrado lógico: la fila se conserva para poder auditar qué se envió.
                notificacion.FechaEliminada = DateTime.Now;
                _ = await db.SaveChangesAsync().ConfigureAwait(false);
                return true;
            }
        }

        private static IQueryable<NotificacionBuzon> ConsultaDelUsuario(NVEntities db, string usuario, string aplicacion)
        {
            return db.NotificacionesBuzon
                .Where(n => n.Usuario == usuario && n.Aplicacion == aplicacion && n.FechaEliminada == null);
        }

        private static NotificacionBuzonDTO AConvertirDTO(NotificacionBuzon notificacion)
        {
            Dictionary<string, string> datos = null;

            if (!string.IsNullOrWhiteSpace(notificacion.Datos))
            {
                try
                {
                    datos = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(notificacion.Datos);
                }
                catch (Newtonsoft.Json.JsonException)
                {
                    // Un JSON corrupto no puede tumbar el buzón entero: se devuelve sin datos.
                    datos = null;
                }
            }

            return new NotificacionBuzonDTO
            {
                Id = notificacion.Id,
                Titulo = notificacion.Titulo,
                Cuerpo = notificacion.Cuerpo,
                Datos = datos,
                FechaCreacion = notificacion.FechaCreacion,
                Leida = notificacion.FechaLeida != null
            };
        }

        private async Task<int> EnviarADispositivos(List<DispositivoNotificacion> dispositivos, NotificacionPushDTO notificacion, string aplicacion)
        {
            if (dispositivos == null || !dispositivos.Any())
            {
                return 0;
            }

            if (!_firebaseInitialized)
            {
                return 0;
            }

            var tokens = dispositivos.Select(d => d.Token).ToList();
            int enviados = 0;

            var message = new MulticastMessage
            {
                Tokens = tokens,
                Notification = new Notification
                {
                    Title = notificacion.Titulo,
                    Body = notificacion.Cuerpo,
                    ImageUrl = notificacion.Datos != null && notificacion.Datos.ContainsKey("imagenUrl")
                        ? notificacion.Datos["imagenUrl"]
                        : null
                },
                Data = notificacion.Datos
            };

            try
            {
                var messaging = ObtenerMessaging(aplicacion);
                if (messaging == null)
                {
                    LogearEnElmah(new Exception($"[Push] No hay instancia Firebase para aplicación: {aplicacion}"));
                    return 0;
                }

                var response = await messaging
                    .SendEachForMulticastAsync(message)
                    .ConfigureAwait(false);

                enviados = response.SuccessCount;

                // Log detallado de cada respuesta para diagnóstico
                for (int i = 0; i < response.Responses.Count; i++)
                {
                    if (!response.Responses[i].IsSuccess)
                    {
                        var ex = response.Responses[i].Exception;
                        LogearEnElmah(new Exception(
                            $"[Push] Token {i} falló. ErrorCode: {ex?.MessagingErrorCode}, " +
                            $"Message: {ex?.Message}, Token: {tokens[i]?.Substring(0, Math.Min(20, tokens[i]?.Length ?? 0))}..."));

                        if (ex?.MessagingErrorCode == MessagingErrorCode.Unregistered)
                        {
                            await DesregistrarDispositivo(tokens[i]).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogearEnElmah(ex);
            }

            return enviados;
        }

        private static void LogearEnElmah(Exception ex)
        {
            try
            {
                ErrorSignal.FromCurrentContext()?.Raise(ex);
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"Firebase Push: {ex.Message}");
            }
        }
    }
}
