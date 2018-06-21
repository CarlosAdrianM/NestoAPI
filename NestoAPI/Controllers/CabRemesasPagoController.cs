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
using System.Text.RegularExpressions;

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
        const int LONGITUD_LINEA = 72;
        int numeroRegistrosTotales = 0;

        // GET: api/CabRemesasPago/5
        [ResponseType(typeof(string))]
        public async Task<IHttpActionResult> GetCrearFicheroRemesa(string empresa, int id)
        {
            CabRemesaPago remesa = await db.CabRemesasPago.FindAsync(empresa, id);
            if (remesa == null)
            {
                return NotFound();
            }

            Empresa empresaRemesa = db.Empresas.SingleOrDefault(e => e.Número == remesa.Empresa);
            Banco banco = db.Bancos.SingleOrDefault(b => b.Empresa == remesa.Empresa && b.Número == remesa.Banco);
            List<ExtractoProveedor> movimientos = db.ExtractosProveedor.Where(e => e.Remesa == remesa.Numero).OrderBy(e => e.Número).ToList();

            
            string lineaFichero = "";
            StringBuilder sb = new StringBuilder();
            DateTime fechaRemesa = (DateTime)remesa.Fecha;
            DateTime fechaFactura;
            


            // Registros de Cabecera del fichero (4 registros obligatorios)
            //*************************************************************

            // Tipo de registro 1: Obligatorio
            lineaFichero = "01";                                            // Código de registro: A
            lineaFichero += "56";                                           // Código de operación: B
            lineaFichero += strLen(empresaRemesa.NIF,10);                   // NIF del ordenante: C
            lineaFichero += new String(' ', 12);                            // Libre: D
            lineaFichero += "001"               ;                           // Número o tipo de dato: E
            lineaFichero += strLen(fechaRemesa.ToString("ddMMyy"), 6);      // Fecha de creación del fichero: F1
            lineaFichero += new String(' ', 6);                             // Libre: F2
            lineaFichero += "2100";                                         // Entidad de destino del soporte: F3
            lineaFichero += "6202";                                         // Oficina de destino del soporte: F4
            lineaFichero += strLen(banco.CodigoConfirming, 10);             // Número de contrato de confirming: F5
            lineaFichero += "1";                                            // Detalle de cargo (0|1): F6
            lineaFichero += "EUR";                                          // Moneda del soporte: F7
            lineaFichero += new string(' ', 2);                             // Libre: F8
            lineaFichero += new string(' ', 7);                             // Libre: F9
            insertarLinea(ref lineaFichero, ref sb);

            // Tipo de registro 2: Obligatorio
            lineaFichero += "01";                                           // Código de registro: A
            lineaFichero += "56";                                           // Código de operación: B
            lineaFichero += strLen(empresaRemesa.NIF, 10);                  // NIF del ordenante: C
            lineaFichero += new String(' ', 12);                            // Libre: D
            lineaFichero += "002";                                          // Número o tipo de dato: E
            lineaFichero += strLen(empresaRemesa.Nombre, 36);               // Nombre del ordenante: F1
            lineaFichero += new String(' ', 7);                             // Libre: F2
            insertarLinea(ref lineaFichero, ref sb);

            // Tipo de registro 3: Obligatorio
            lineaFichero += "01";                                           // Código de registro: A
            lineaFichero += "56";                                           // Código de operación: B
            lineaFichero += strLen(empresaRemesa.NIF, 10);                  // NIF del ordenante: C
            lineaFichero += new String(' ', 12);                            // Libre: D
            lineaFichero += "003";                                          // Número o tipo de dato: E
            lineaFichero += strLen(empresaRemesa.Dirección, 36);            // Domicilio del ordenante: F1
            lineaFichero += new String(' ', 7);                             // Libre: F2
            insertarLinea(ref lineaFichero, ref sb);

            // Tipo de registro 4: Obligatorio
            lineaFichero += "01";                                           // Código de registro: A
            lineaFichero += "56";                                           // Código de operación: B
            lineaFichero += strLen(empresaRemesa.NIF, 10);                  // NIF del ordenante: C
            lineaFichero += new String(' ', 12);                            // Libre: D
            lineaFichero += "004";                                          // Número o tipo de dato: E
            lineaFichero += strLen(empresaRemesa.Población, 36);            // Plaza del ordenante: F1
            lineaFichero += new String(' ', 7);                             // Libre: F2
            insertarLinea(ref lineaFichero, ref sb);


            // Registros del beneficiario (5 registros obligatorios, 9 opcionales)
            //********************************************************************

            Proveedor proveedor;
            string correo;
            CabFacturaCmp factura;
            decimal sumaFacturas = 0;
            int numeroRegistrosFactura = 0;
            

            foreach (ExtractoProveedor efecto in movimientos)
            {
                proveedor = db.Proveedores.Include(p => p.CCCProveedore).Include(p=>p.PersonasContactoProveedors).SingleOrDefault(p => p.Empresa == empresa && p.Número == efecto.Número && p.ProveedorPrincipal);
                factura = db.CabFacturasCmp.SingleOrDefault(f=> f.Empresa == efecto.Empresa && f.Número == efecto.NºDocumento);
                sumaFacturas -= efecto.Importe;

                try {
                    numeroRegistrosFactura++;
                    string ibanNoResidente = proveedor.CCCProveedore.IbanNoResidente != null ? proveedor.CCCProveedore.IbanNoResidente.Trim() : "";
                    string idProveedor = ibanNoResidente == "" ? efecto.CIF_NIF.Trim() : proveedor.Número.Trim();

                    // Tipo de registro 10: Obligatorio
                    lineaFichero += "06";                                           // Código de registro: A
                    lineaFichero += "56";                                           // Código de operación: B
                    lineaFichero += strLen(empresaRemesa.NIF, 10);                  // NIF del ordenante: C
                    lineaFichero += strLen(idProveedor,12);                         // NIF del proveedor o ref. interna: D
                    lineaFichero += "010";                                          // Número o tipo de dato: E
                    lineaFichero += intLen(-efecto.Importe, 12);                    // Importe con 2 decimales: F1
                    lineaFichero += strLen(proveedor.CCCProveedore.Entidad, 4);     // Número de entidad de crédito receptora: F2
                    lineaFichero += strLen(proveedor.CCCProveedore.Oficina, 4);     // Número de sucursal de crédito receptora: F3
                    lineaFichero += strLen(proveedor.CCCProveedore.Nº_Cuenta, 10);  // Número de la cuenta abono: F4
                    lineaFichero += "1";                                            // Gastos por cuenta del ordenante: F5
                    lineaFichero += "9";                                            // Concepto de la orden: F9
                    lineaFichero += new String(' ', 2);                             // Libre: F7
                    lineaFichero += strLen(proveedor.CCCProveedore.DC, 2);          // Dígitos CCC de la cuenta de proveedor: F8
                    lineaFichero += ibanNoResidente == "" ? "N" : "S";              // Indicador de proveedor no residente: F9
                    lineaFichero += "C";                                            // Indicador de confirmación: F10
                    lineaFichero += "EUR";                                          // Moneda de la factura: F11
                    lineaFichero += new String(' ', 2);                             // Libre: F12
                    insertarLinea(ref lineaFichero, ref sb);

                    // Tipo de registro 43: Obligatorio solo para internacional
                    if (ibanNoResidente != "")
                    {
                        lineaFichero += "06";                                           // Código de registro: A
                        lineaFichero += "56";                                           // Código de operación: B
                        lineaFichero += strLen(empresaRemesa.NIF, 10);                  // NIF del ordenante: C
                        lineaFichero += strLen(idProveedor, 12);                        // NIF del proveedor: D (pongo el número porque en el registro 10 lo pide así)
                        lineaFichero += "043";                                          // Número o tipo de dato: E
                        lineaFichero += strLen(ibanNoResidente, 34);                    // Nombre del proveedor: F1
                        lineaFichero += "7";                                            // Concepto de la orden: F2
                        lineaFichero += new String(' ', 8);                             // Libre: F3
                        insertarLinea(ref lineaFichero, ref sb);
                    }

                    // Tipo de registro 44: Obligatorio solo para internacional
                    if (ibanNoResidente != "")
                    {
                        lineaFichero += "06";                                           // Código de registro: A
                        lineaFichero += "56";                                           // Código de operación: B
                        lineaFichero += strLen(empresaRemesa.NIF, 10);                  // NIF del ordenante: C
                        lineaFichero += strLen(idProveedor, 12);                        // NIF del proveedor: D (pongo el número porque en el registro 10 lo pide así)
                        lineaFichero += "044";                                          // Número o tipo de dato: E
                        lineaFichero += "1";                                            // Clave de gastos: F1
                        lineaFichero += ibanNoResidente.Substring(0,2);                 // Código ISO país destino: F2
                        lineaFichero += new String(' ', 6);                             // Libre: F3
                        lineaFichero += strLen(proveedor.CCCProveedore.Swift, 12);      // Código SWIFT: F4
                        lineaFichero += new String(' ', 22);                            // Libre: F5
                        insertarLinea(ref lineaFichero, ref sb);
                    }

                    // Tipo de registro 11: Obligatorio
                    lineaFichero += "06";                                           // Código de registro: A
                    lineaFichero += "56";                                           // Código de operación: B
                    lineaFichero += strLen(empresaRemesa.NIF, 10);                  // NIF del ordenante: C
                    lineaFichero += strLen(idProveedor, 12);                        // NIF del proveedor: D
                    lineaFichero += "011";                                          // Número o tipo de dato: E
                    lineaFichero += strLen(proveedor.Nombre, 36);                   // Nombre del proveedor: F1
                    lineaFichero += new String(' ', 7);                             // Libre: F2
                    insertarLinea(ref lineaFichero, ref sb);

                    // Tipo de registro 12: Obligatorio
                    lineaFichero += "06";                                           // Código de registro: A
                    lineaFichero += "56";                                           // Código de operación: B
                    lineaFichero += strLen(empresaRemesa.NIF, 10);                  // NIF del ordenante: C
                    lineaFichero += strLen(idProveedor, 12);                        // NIF del proveedor: D
                    lineaFichero += "012";                                          // Número o tipo de dato: E
                    lineaFichero += strLen(proveedor.Dirección, 36);                // Domicilio del proveedor: F1
                    lineaFichero += new String(' ', 7);                             // Libre: F2
                    insertarLinea(ref lineaFichero, ref sb);

                    // Tipo de registro 14: Obligatorio
                    lineaFichero += "06";                                           // Código de registro: A
                    lineaFichero += "56";                                           // Código de operación: B
                    lineaFichero += strLen(empresaRemesa.NIF, 10);                  // NIF del ordenante: C
                    lineaFichero += strLen(idProveedor, 12);                        // NIF del proveedor: D
                    lineaFichero += "014";                                          // Número o tipo de dato: E
                    lineaFichero += strLen(proveedor.CodPostal, 5);                 // Código postal del proveedor: F1
                    lineaFichero += strLen(proveedor.Población, 31);                // Plaza del proveedor: F1
                    lineaFichero += new String(' ', 7);                             // Libre: F3
                    insertarLinea(ref lineaFichero, ref sb);

                    // Tipo de registro 15: Obligatorio solo para no residentes
                    if (ibanNoResidente != "")
                    {
                        lineaFichero += "06";                                           // Código de registro: A
                        lineaFichero += "56";                                           // Código de operación: B
                        lineaFichero += strLen(empresaRemesa.NIF, 10);                  // NIF del ordenante: C
                        lineaFichero += strLen(idProveedor, 12);                        // NIF del proveedor: D (pongo el número porque en el registro 10 lo pide así)
                        lineaFichero += "015";                                          // Número o tipo de dato: E
                        lineaFichero += strLen(empresaRemesa.NIF, 15);                  // Código del proveedor que identifica al cliente: F1
                        lineaFichero += new string(' ', 12);                            // NIF proveedor si la factura está endosada: F2 (no lo informamos)
                        lineaFichero += new String(' ', 1);                             // Clasificación del proveedor: F3
                        lineaFichero += ibanNoResidente.Substring(0, 2);                // Código ISO país destino: F4
                        lineaFichero += ibanNoResidente.Substring(0, 9);                // País destino: F5
                        lineaFichero += new String(' ', 4);                             // Libre: F6
                        insertarLinea(ref lineaFichero, ref sb);
                    }

                    // Tipo de registro 16: Obligatorio
                    lineaFichero += "06";                                           // Código de registro: A
                    lineaFichero += "56";                                           // Código de operación: B
                    lineaFichero += strLen(empresaRemesa.NIF, 10);                  // NIF del ordenante: C
                    lineaFichero += strLen(idProveedor, 12);                        // NIF del proveedor: D
                    lineaFichero += "016";                                          // Número o tipo de dato: E
                    lineaFichero += "T";                                            // Forma de pago. C=Cheque. T=Transferencia: F1
                    fechaFactura = (DateTime)factura.FechaProveedor;
                    lineaFichero += strLen(fechaFactura.ToString("ddMMyy"), 6);     // Fecha de la factura: F2
                    lineaFichero += strLen(efecto!=null ? efecto.NºDocumentoProv : "", 15);             // Número de la factura: F3
                    lineaFichero += strLen(efecto != null ? efecto.Fecha.ToString("ddMMyy") : DateTime.Today.ToString("ddMMyy"), 6);     // Fecha de vencimiento de la factura: F4
                    lineaFichero += new String(' ', 8);                             // Libre: F5
                    lineaFichero += new String(' ', 7);                             // Libre: F6
                    insertarLinea(ref lineaFichero, ref sb);

                    // Tipo de registro 18: Obligatorio solo para no residentes
                    if (ibanNoResidente != "")
                    {
                        lineaFichero += "06";                                           // Código de registro: A
                        lineaFichero += "56";                                           // Código de operación: B
                        lineaFichero += strLen(empresaRemesa.NIF, 10);                  // NIF del ordenante: C
                        lineaFichero += strLen(idProveedor, 12);                        // NIF del proveedor: D (pongo el número porque en el registro 10 lo pide así)
                        lineaFichero += "018";                                          // Número o tipo de dato: E
                        lineaFichero += strLen(proveedor.Teléfono, 15);                 // Teléfono proveedor: F1
                        lineaFichero += strLen(proveedor.Fax, 15);                      // Fax proveedor: F1
                        lineaFichero += new String(' ', 6);                             // Libre: F3
                        lineaFichero += new String(' ', 7);                             // Libre: F4
                        insertarLinea(ref lineaFichero, ref sb);
                    }

                    // Tipo de registro 19: Opcional
                    lineaFichero += "06";                                           // Código de registro: A
                    lineaFichero += "56";                                           // Código de operación: B
                    lineaFichero += strLen(empresaRemesa.NIF, 10);                  // NIF del ordenante: C
                    lineaFichero += strLen(idProveedor, 12);                        // NIF del proveedor: D
                    lineaFichero += "019";                                          // Número o tipo de dato: E
                    correo = proveedor.PersonasContactoProveedors.SingleOrDefault(p => p.Cargo == 15).CorreoElectrónico;
                    lineaFichero += correo.PadRight(36).Substring(0,36);            // Correo electrónico del proveedor: F1
                    lineaFichero += new String(' ', 7);                             // Libre: F2
                    insertarLinea(ref lineaFichero, ref sb);

                } catch (Exception ex)
                {
                    throw ex;
                }



            }

            // Registro de totales (1 registro obligatorio)
            //*********************************************
            // Tipo de registro único: Obligatorio
            numeroRegistrosTotales++;                                       // Porque cuando se incrementa ya hemos puesto el número
            lineaFichero += "08";                                           // Código de registro: A
            lineaFichero += "56";                                           // Código de operación: B
            lineaFichero += strLen(empresaRemesa.NIF, 10);                  // NIF del ordenante: C
            lineaFichero += new String(' ', 12);                            // Libre: D
            lineaFichero += new String(' ', 3);                             // Libre: E
            lineaFichero += intLen(sumaFacturas, 12);                       // Suma importe de las facturas: F1
            lineaFichero += numeroRegistrosFactura.ToString().PadLeft(8, '0').Substring(0, 8);// Número de registros del tipo 010: F2
            lineaFichero += numeroRegistrosTotales.ToString().PadLeft(10, '0').Substring(0, 10);// Número de registros totales: F3
            lineaFichero += new String(' ', 6);                             // Libre: F4
            lineaFichero += new String(' ', 7);                             // Libre: F5
            insertarLinea(ref lineaFichero, ref sb);

            // Guardamos el fichero
            string nombreFichero = String.Format("\\\\diskstation\\datos\\Banco\\Confirming\\E{0}R{1}.txt", remesa.Empresa.Trim(), remesa.Numero.ToString().Trim());
            using (StreamWriter outfile = new StreamWriter(nombreFichero))
            {
                outfile.Write(sb.ToString());
            }

            return Ok(nombreFichero);
        }

        private void insertarLinea(ref string linea, ref StringBuilder sb)
        {
            if (linea.Length != LONGITUD_LINEA)
            {
                throw new Exception("Longitud incorrecta en el registro 1");
            }

            // insertar en el StringBuilder
            sb.AppendLine(linea);
            numeroRegistrosTotales++;
            linea = "";
        }

        private string strLen(string cadena, int longitud)
        {
            if (cadena == null)
            {
                cadena = "";
            }
            string textoNormalizado = cadena.Normalize(NormalizationForm.FormD);
            Regex reg = new Regex("[^a-zA-Z0-9 ]");
            string textoSinAcentos = reg.Replace(textoNormalizado, "");
            return textoSinAcentos.ToUpper().PadRight(longitud).Substring(0, longitud);
        }

        private string intLen(decimal importe, int longitud)
        {
            int importeDosDecimales = (int)(importe * 100);
            return importeDosDecimales.ToString().PadLeft(longitud, '0').Substring(0, longitud);
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