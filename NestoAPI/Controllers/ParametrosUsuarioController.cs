﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Models;

namespace NestoAPI.Controllers
{
    public class ParametrosUsuarioController : ApiController
    {
        // Carlos 08/09/15: lo pongo para desactivar el Lazy Loading
        public ParametrosUsuarioController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }

        private NVEntities db = new NVEntities();

        /*
        // GET: api/ParametrosUsuario
        public IQueryable<ParametroUsuario> GetParametrosUsuario()
        {
            return db.ParametrosUsuario;
        }
        */

        // GET: api/ParametrosUsuario/5
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> GetParametroUsuario(string empresa, string usuario, string clave)
        {
            ParametroUsuario parametroUsuario = db.ParametrosUsuario.FirstOrDefault(p => p.Empresa == empresa && p.Usuario == usuario && p.Clave == clave);
            if (parametroUsuario != null)
            {
                return Ok(parametroUsuario.Valor != null ? parametroUsuario.Valor.Trim() : "");
            }

            // Si el parámetro no existe, buscamos el del usuario por defecto y lo creamos
            parametroUsuario = db.ParametrosUsuario.FirstOrDefault(p => p.Empresa == empresa && p.Usuario == "(defecto)" && p.Clave == clave);
            if (parametroUsuario != null)
            {
                ParametroUsuario parametroInsertar = new ParametroUsuario
                {
                    Empresa = empresa,
                    Usuario = usuario,
                    Clave = clave,
                    Valor = parametroUsuario.Valor,
                    Usuario2 = usuario,
                    Fecha_Modificación = DateTime.Now
                };
                db.ParametrosUsuario.Add(parametroInsertar);
                try
                {
                    await db.SaveChangesAsync();
                } catch (Exception ex)
                {
                    throw ex;
                }
                
                return Ok(parametroUsuario.Valor);
            }

            // No debería suceder nunca, porque siempre existe el usuario por defecto
            return NotFound();
            
        }

        public string Leer(string empresa, string usuario, string clave)
        {
            Task<IHttpActionResult> tarea = GetParametroUsuario(empresa, usuario, clave);
            return tarea.Result.ToString();
        }

        public static string LeerParametro(string empresa, string usuario, string clave)
        {
            NVEntities db = new NVEntities();
            ParametroUsuario parametroUsuario;
            string usuarioParametro = usuario.Substring(usuario.IndexOf("\\") + 1);
            if (usuarioParametro != null)
            {
                parametroUsuario = db.ParametrosUsuario.SingleOrDefault(p => p.Empresa == empresa && p.Usuario == usuarioParametro && p.Clave == clave);
                return parametroUsuario?.Valor?.Trim();
            };

            return "";
        }

        
        // PUT: api/ParametrosUsuario/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutParametroUsuario(ParametroUsuario parametro)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }
            
            db.Entry(parametro).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ParametroUsuarioExists(parametro.Empresa, parametro.Usuario, parametro.Clave))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return StatusCode(HttpStatusCode.NoContent);
        }
        /*
        // POST: api/ParametrosUsuario
        [ResponseType(typeof(ParametroUsuario))]
        public async Task<IHttpActionResult> PostParametroUsuario(ParametroUsuario parametroUsuario)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.ParametrosUsuario.Add(parametroUsuario);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (ParametroUsuarioExists(parametroUsuario.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = parametroUsuario.Empresa }, parametroUsuario);
        }

        // DELETE: api/ParametrosUsuario/5
        [ResponseType(typeof(ParametroUsuario))]
        public async Task<IHttpActionResult> DeleteParametroUsuario(string id)
        {
            ParametroUsuario parametroUsuario = await db.ParametrosUsuario.FindAsync(id);
            if (parametroUsuario == null)
            {
                return NotFound();
            }

            db.ParametrosUsuario.Remove(parametroUsuario);
            await db.SaveChangesAsync();

            return Ok(parametroUsuario);
        }
        */
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool ParametroUsuarioExists(string empresa, string usuario, string clave)
        {
            return db.ParametrosUsuario.Any(e => e.Empresa == empresa && e.Usuario == usuario && e.Clave == clave);
        }

    }
}