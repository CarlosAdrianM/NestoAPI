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

        public static bool EstaInicializado => _firebaseInitialized;

        public ServicioNotificacionesPush()
        {
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

                try
                {
                    string credentialPath = BuscarCredenciales();
                    if (credentialPath != null)
                    {
                        FirebaseApp.Create(new AppOptions
                        {
                            Credential = GoogleCredential.FromFile(credentialPath)
                        });
                        _firebaseInitialized = true;
                    }
                    else
                    {
                        string rutasBuscadas = ObtenerRutasBuscadas();
                        LogearEnElmah(new FileNotFoundException(
                            $"No se encontró el fichero de credenciales de Firebase. Rutas buscadas: {rutasBuscadas}"));
                    }
                }
                catch (Exception ex)
                {
                    LogearEnElmah(new Exception($"Error inicializando Firebase Admin SDK: {ex.Message}", ex));
                }
            }
        }

        private const string NOMBRE_FICHERO_CREDENCIALES = "firebase-adminsdk-nestoapp.json";

        private static string BuscarCredenciales()
        {
            // 1. appSettings explícito
            string ruta = ConfigurationManager.AppSettings["FirebaseServiceAccountKeyPath"];
            if (!string.IsNullOrWhiteSpace(ruta) && File.Exists(ruta))
            {
                return ruta;
            }

            // 2. Secrets/firebase-adminsdk-nestoapp.json relativo a la app
            string baseDir = HostingEnvironment.MapPath("~/Secrets");
            if (baseDir != null)
            {
                string rutaRelativa = Path.Combine(baseDir, NOMBRE_FICHERO_CREDENCIALES);
                if (File.Exists(rutaRelativa))
                {
                    return rutaRelativa;
                }
            }

            return null;
        }

        private static string ObtenerRutasBuscadas()
        {
            string rutaAppSettings = ConfigurationManager.AppSettings["FirebaseServiceAccountKeyPath"] ?? "(no configurada)";
            string baseDir = HostingEnvironment.MapPath("~/Secrets") ?? "(no disponible)";
            string rutaRelativa = baseDir != "(no disponible)"
                ? Path.Combine(baseDir, NOMBRE_FICHERO_CREDENCIALES)
                : "(no disponible)";

            return $"1) appSettings 'FirebaseServiceAccountKeyPath': {rutaAppSettings} | 2) Ruta relativa: {rutaRelativa}";
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

            using (NVEntities db = new NVEntities())
            {
                var existente = await db.DispositivosNotificaciones
                    .FirstOrDefaultAsync(d => d.Token == registro.Token)
                    .ConfigureAwait(false);

                if (existente != null)
                {
                    existente.Usuario = usuario;
                    existente.Empresa = registro.Empresa;
                    existente.Vendedor = registro.Vendedor;
                    existente.Cliente = registro.Cliente;
                    existente.Contacto = registro.Contacto;
                    existente.Plataforma = registro.Plataforma;
                    existente.Aplicacion = registro.Aplicacion;
                    existente.FechaUltimaActividad = DateTime.Now;
                    existente.Activo = true;

                    await db.SaveChangesAsync().ConfigureAwait(false);
                    return existente;
                }

                var nuevo = new DispositivoNotificacion
                {
                    Usuario = usuario,
                    Empresa = registro.Empresa,
                    Vendedor = registro.Vendedor,
                    Cliente = registro.Cliente,
                    Contacto = registro.Contacto,
                    Token = registro.Token,
                    Plataforma = registro.Plataforma,
                    Aplicacion = registro.Aplicacion,
                    FechaRegistro = DateTime.Now,
                    FechaUltimaActividad = DateTime.Now,
                    Activo = true
                };

                db.DispositivosNotificaciones.Add(nuevo);
                await db.SaveChangesAsync().ConfigureAwait(false);
                return nuevo;
            }
        }

        public async Task<bool> DesregistrarDispositivo(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            using (NVEntities db = new NVEntities())
            {
                var dispositivo = await db.DispositivosNotificaciones
                    .FirstOrDefaultAsync(d => d.Token == token)
                    .ConfigureAwait(false);

                if (dispositivo == null)
                {
                    return false;
                }

                dispositivo.Activo = false;
                await db.SaveChangesAsync().ConfigureAwait(false);
                return true;
            }
        }

        public async Task<List<DispositivoNotificacion>> ObtenerDispositivosUsuario(string usuario, string aplicacion)
        {
            using (NVEntities db = new NVEntities())
            {
                return await db.DispositivosNotificaciones
                    .Where(d => d.Usuario == usuario && d.Aplicacion == aplicacion && d.Activo)
                    .ToListAsync()
                    .ConfigureAwait(false);
            }
        }

        public async Task<List<DispositivoNotificacion>> ObtenerDispositivosVendedor(string empresa, string vendedor, string aplicacion)
        {
            using (NVEntities db = new NVEntities())
            {
                return await db.DispositivosNotificaciones
                    .Where(d => d.Empresa == empresa && d.Vendedor == vendedor && d.Aplicacion == aplicacion && d.Activo)
                    .ToListAsync()
                    .ConfigureAwait(false);
            }
        }

        public async Task<List<DispositivoNotificacion>> ObtenerDispositivosCliente(string empresa, string cliente, string aplicacion)
        {
            using (NVEntities db = new NVEntities())
            {
                return await db.DispositivosNotificaciones
                    .Where(d => d.Empresa == empresa && d.Cliente == cliente && d.Aplicacion == aplicacion && d.Activo)
                    .ToListAsync()
                    .ConfigureAwait(false);
            }
        }

        public async Task<int> EnviarAUsuario(string usuario, string aplicacion, NotificacionPushDTO notificacion)
        {
            var dispositivos = await ObtenerDispositivosUsuario(usuario, aplicacion).ConfigureAwait(false);
            return await EnviarADispositivos(dispositivos, notificacion).ConfigureAwait(false);
        }

        public async Task<int> EnviarAVendedor(string empresa, string vendedor, NotificacionPushDTO notificacion)
        {
            var dispositivos = await ObtenerDispositivosVendedor(empresa, vendedor, Constantes.Aplicaciones.NESTO_APP).ConfigureAwait(false);
            return await EnviarADispositivos(dispositivos, notificacion).ConfigureAwait(false);
        }

        public async Task<int> EnviarACliente(string empresa, string cliente, NotificacionPushDTO notificacion)
        {
            var dispositivos = await ObtenerDispositivosCliente(empresa, cliente, Constantes.Aplicaciones.NESTO_TIENDAS).ConfigureAwait(false);
            return await EnviarADispositivos(dispositivos, notificacion).ConfigureAwait(false);
        }

        public async Task<int> EnviarATodosDeAplicacion(string aplicacion, NotificacionPushDTO notificacion)
        {
            using (NVEntities db = new NVEntities())
            {
                var dispositivos = await db.DispositivosNotificaciones
                    .Where(d => d.Aplicacion == aplicacion && d.Activo)
                    .ToListAsync()
                    .ConfigureAwait(false);

                return await EnviarADispositivos(dispositivos, notificacion).ConfigureAwait(false);
            }
        }

        private async Task<int> EnviarADispositivos(List<DispositivoNotificacion> dispositivos, NotificacionPushDTO notificacion)
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
                    Body = notificacion.Cuerpo
                },
                Data = notificacion.Datos
            };

            try
            {
                var response = await FirebaseMessaging.DefaultInstance
                    .SendEachForMulticastAsync(message)
                    .ConfigureAwait(false);

                enviados = response.SuccessCount;

                // Desactivar tokens inválidos
                for (int i = 0; i < response.Responses.Count; i++)
                {
                    if (!response.Responses[i].IsSuccess &&
                        response.Responses[i].Exception?.MessagingErrorCode == MessagingErrorCode.Unregistered)
                    {
                        await DesregistrarDispositivo(tokens[i]).ConfigureAwait(false);
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
