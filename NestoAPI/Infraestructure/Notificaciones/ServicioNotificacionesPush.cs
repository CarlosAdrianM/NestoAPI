using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Notificaciones
{
    public class ServicioNotificacionesPush : IServicioNotificacionesPush
    {
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
    }
}
