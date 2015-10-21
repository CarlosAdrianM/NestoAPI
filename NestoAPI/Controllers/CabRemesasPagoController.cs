using System;
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
using System.Text;
using System.IO;

namespace NestoAPI.Controllers
{
    public class CabRemesasPagoController : ApiController
    {
        // Carlos 19/10/15: lo pongo para desactivar el Lazy Loading
        public CabRemesasPagoController()
        {
            db.Configuration.LazyLoadingEnabled = false;
        }

        private NVEntities db = new NVEntities();

        // GET: api/CabRemesasPago/5
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> GetCrearFicheroRemesa(string id, string rutaFichero)
        {
            CabRemesaPago remesa = await db.CabRemesasPago.FindAsync(id);
            if (remesa == null)
            {
                return NotFound();
            }

            Empresa empresa = db.Empresas.SingleOrDefault(e => e.Número == remesa.Empresa);
            Banco banco = db.Bancos.SingleOrDefault(b => b.Empresa == remesa.Empresa && b.Número == remesa.Banco);
            List<ExtractoProveedor> movimientos = db.ExtractosProveedor.Where(e => e.Remesa == remesa.Numero).OrderBy(e => e.Número).ToList();

            const int LONGITUD_LINEA = 72;
            string lineaFichero = "";
            StringBuilder sb = new StringBuilder();
            DateTime fechaRemesa = (DateTime)remesa.Fecha;


            // Registros de Cabecera del fichero (4 registros obligatorios)
            //*************************************************************

            // Tipo de registro 1: Obligatorio
            lineaFichero = "01";                                            // Código de registro: A
            lineaFichero += "56";                                           // Código de operación: B
            lineaFichero += empresa.NIF.PadRight(10);                       // NIF del ordenante: C
            lineaFichero += new String(' ', 12);                            // Libre: D
            lineaFichero += "001"               ;                           // Número o tipo de dato: E
            lineaFichero += fechaRemesa.ToString("DDMMyy").PadRight(6);     // Fecha de creación del fichero: F1
            lineaFichero += new String(' ', 6);                             // Libre: F2
            lineaFichero += "2100";                                         // Entidad de destino del soporte: F3
            lineaFichero += "6202";                                         // Oficina de destino del soporte: F4
            lineaFichero += banco.CodigoConfirming.PadRight(10);            // Número de contrato de confirming: F5
            lineaFichero += "1";                                            // Detalle de cargo (0|1): F6
            lineaFichero += "EUR";                                          // Moneda del soporte: F7
            lineaFichero += new string(' ', 2);                             // Libre: F8
            lineaFichero += new string(' ', 7);                             // Libre: F9
            
            if (lineaFichero.Length != LONGITUD_LINEA)
            {
                throw new Exception("Longitud incorrecta en el registro 1");
            }
            
            // insertar en el StringBuilder
            sb.AppendLine(lineaFichero);
            lineaFichero = "";

            // Tipo de registro 2: Obligatorio
            lineaFichero += "01";                                           // Código de registro: A
            lineaFichero += "56";                                           // Código de operación: B
            lineaFichero += empresa.NIF.PadRight(10);                       // NIF del ordenante: C
            lineaFichero += new String(' ', 12);                            // Libre: D
            lineaFichero += "002";                                          // Número o tipo de dato: E
            lineaFichero += empresa.Nombre.ToUpper().PadRight(36);          // Nombre del ordenante: F1
            lineaFichero += new String(' ', 7);                            // Libre: F2

            if (lineaFichero.Length != LONGITUD_LINEA)
            {
                throw new Exception("Longitud incorrecta en el registro 1");
            }

            // insertar en el StringBuilder
            sb.AppendLine(lineaFichero);
            lineaFichero = "";



            // Registros del beneficiario (5 registros obligatorios, 9 opcionales)
            //********************************************************************


            // Registro de totales (1 registro obligatorio)
            //*********************************************


            // Guardamos el fichero
            string nombreFichero = "F:\\BANCO\\Confirming\\Prueba.txt";
            using (StreamWriter outfile = new StreamWriter(nombreFichero))
            {
                outfile.Write(sb.ToString());
            }

            return Ok(nombreFichero);
        }

        /*
        // GET: api/CabRemesasPago
        public IQueryable<CabRemesaPago> GetCabRemesasPago()
        {
            return db.CabRemesasPago;
        }

        // GET: api/CabRemesasPago/5
        [ResponseType(typeof(CabRemesaPago))]
        public async Task<IHttpActionResult> GetCabRemesaPago(string id)
        {
            CabRemesaPago cabRemesaPago = await db.CabRemesasPago.FindAsync(id);
            if (cabRemesaPago == null)
            {
                return NotFound();
            }

            return Ok(cabRemesaPago);
        }

        // PUT: api/CabRemesasPago/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutCabRemesaPago(string id, CabRemesaPago cabRemesaPago)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != cabRemesaPago.Empresa)
            {
                return BadRequest();
            }

            db.Entry(cabRemesaPago).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CabRemesaPagoExists(id))
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

        // POST: api/CabRemesasPago
        [ResponseType(typeof(CabRemesaPago))]
        public async Task<IHttpActionResult> PostCabRemesaPago(CabRemesaPago cabRemesaPago)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.CabRemesasPago.Add(cabRemesaPago);

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                if (CabRemesaPagoExists(cabRemesaPago.Empresa))
                {
                    return Conflict();
                }
                else
                {
                    throw;
                }
            }

            return CreatedAtRoute("DefaultApi", new { id = cabRemesaPago.Empresa }, cabRemesaPago);
        }

        // DELETE: api/CabRemesasPago/5
        [ResponseType(typeof(CabRemesaPago))]
        public async Task<IHttpActionResult> DeleteCabRemesaPago(string id)
        {
            CabRemesaPago cabRemesaPago = await db.CabRemesasPago.FindAsync(id);
            if (cabRemesaPago == null)
            {
                return NotFound();
            }

            db.CabRemesasPago.Remove(cabRemesaPago);
            await db.SaveChangesAsync();

            return Ok(cabRemesaPago);
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

        private bool CabRemesaPagoExists(string id)
        {
            return db.CabRemesasPago.Count(e => e.Empresa == id) > 0;
        }
    }
}