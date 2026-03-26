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
            { Constantes.Aplicaciones.NESTO_TIENDAS, "firebase-adminsdk-nestotiendas.json" }
        };

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
            return await EnviarADispositivos(dispositivos, notificacion, aplicacion).ConfigureAwait(false);
        }

        public async Task<int> EnviarAVendedor(string empresa, string vendedor, NotificacionPushDTO notificacion)
        {
            string aplicacion = Constantes.Aplicaciones.NESTO_APP;
            var dispositivos = await ObtenerDispositivosVendedor(empresa, vendedor, aplicacion).ConfigureAwait(false);
            return await EnviarADispositivos(dispositivos, notificacion, aplicacion).ConfigureAwait(false);
        }

        public async Task<int> EnviarACliente(string empresa, string cliente, NotificacionPushDTO notificacion)
        {
            string aplicacion = Constantes.Aplicaciones.NESTO_TIENDAS;
            var dispositivos = await ObtenerDispositivosCliente(empresa, cliente, aplicacion).ConfigureAwait(false);
            return await EnviarADispositivos(dispositivos, notificacion, aplicacion).ConfigureAwait(false);
        }

        public async Task<int> EnviarATodosDeAplicacion(string aplicacion, NotificacionPushDTO notificacion)
        {
            using (NVEntities db = new NVEntities())
            {
                var dispositivos = await db.DispositivosNotificaciones
                    .Where(d => d.Aplicacion == aplicacion && d.Activo)
                    .ToListAsync()
                    .ConfigureAwait(false);

                return await EnviarADispositivos(dispositivos, notificacion, aplicacion).ConfigureAwait(false);
            }
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
