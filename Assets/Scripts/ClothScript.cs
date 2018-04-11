using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Experimental.Playables;

public class ClothScript : MonoBehaviour
{
    /// <summary>
    /// Spring class for declaring a spring </summary>
    class Spring
    {
        public List<int> vector1s, vector2s;
        public float restLength;

        public Spring(List<int> vector1RefPoint, List<int> vector2RefPoint, float resLength)
        {
            vector1s = vector1RefPoint;
            vector2s = vector2RefPoint;
            restLength = resLength;
        }
    }

    public float KStretching = 10f;
    public float KDamping = 1f;
    public float Mass = 1;
    public bool UseHangingStaticPoints;
    public List<int> StaticVertices;
    public List<Transform> StaticPoints;

    private Mesh mesh;
    private List<Spring> springs;
    private Vector3[] forces;
    private Vector3[] velocities;
    private Dictionary<Vector3, List<int>> filteredStartPositions; //Needed to convert from an unity-mesh (multiple vertices per vertex) to our own model (one vertex per vertex)
    private Vector3[] prevPos;
    private List<int> staticVertices;
    private bool pauze = true;
    private List<SphereCollider> _collidingSpheres;
    private Vector3[] meshVertices;

    /// <summary>
    /// Initialisations </summary>
    void Start()
    {
        mesh = gameObject.GetComponent<MeshFilter>().mesh;
        springs = new List<Spring>();
        forces = new Vector3[mesh.vertexCount];
        velocities = new Vector3[mesh.vertexCount];
        prevPos = new Vector3[mesh.vertexCount];
        prevPos = mesh.vertices;
        filteredStartPositions = new Dictionary<Vector3, List<int>>();
        staticVertices = new List<int>();
        _collidingSpheres = new List<SphereCollider>();


        for (int i = 0; i < mesh.vertices.Length; i++)
        {
            if (!filteredStartPositions.ContainsKey(mesh.vertices[i]))
            {
                filteredStartPositions.Add(mesh.vertices[i], new List<int> { i });
            }
            else
            {
                filteredStartPositions[mesh.vertices[i]].Add(i);
            }
        }

        for (int i = 0; i < mesh.triangles.Length; i += 3)
        {
            int v1 = mesh.triangles[i];
            int v2 = mesh.triangles[i + 1];
            int v3 = mesh.triangles[i + 2];

            List<int> allV1Ints = filteredStartPositions[mesh.vertices[v1]];
            List<int> allV2Ints = filteredStartPositions[mesh.vertices[v2]];
            List<int> allV3Ints = filteredStartPositions[mesh.vertices[v3]];

            SetNewSpring(allV1Ints, allV2Ints, Vector3.Distance(mesh.vertices[v1], mesh.vertices[v2]));
            SetNewSpring(allV2Ints, allV3Ints, Vector3.Distance(mesh.vertices[v2], mesh.vertices[v3]));
            SetNewSpring(allV3Ints, allV1Ints, Vector3.Distance(mesh.vertices[v3], mesh.vertices[v1]));

        }

        //Draw the springs
        //DrawSprings(100f);
    }

    /// <summary>
    /// Create a new spring and add it to the system </summary>
    private void SetNewSpring(List<int> v1s, List<int> v2s, float l)
    {
        foreach (Spring spring in springs)
        {
            if ((spring.vector1s == v1s && spring.vector2s == v2s) || (spring.vector1s == v2s && spring.vector2s == v1s))
                return;
        }
        springs.Add(new Spring(v1s, v2s, l));
    }


    /// <summary>
    /// User/externe input on the system </summary>
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.A))
        {
            DrawSprings(10f);
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            pauze = !pauze;
        }

        if (Input.GetKeyDown(KeyCode.U) && UseHangingStaticPoints)
        {
            for (int i = 0; i < StaticVertices.Count; i++)
            {
                int staticVertex = StaticVertices[i];
                staticVertices.Add(staticVertex);
                prevPos[staticVertex] = transform.InverseTransformPoint(StaticPoints[i].position);
            }
        }
    }

    /// <summary>
    /// Main loop for the cloth simulation </summary>
    void FixedUpdate()
    {
        if (pauze) return;

        meshVertices = mesh.vertices;

        for (int i = 0; i < forces.Length; i++)
        {
            forces[i] = new Vector3(0, 0, 0);
            forces[i].y -= 9.81f / 1;

            forces[i] -= 0.5f * velocities[i];
        }

        foreach (Spring spring in springs)
        {
            float currentDistance = Vector3.Distance(meshVertices[spring.vector2s[0]], meshVertices[spring.vector1s[0]]);

            float springStretching = -KStretching * (currentDistance - spring.restLength);
            float springDempening = -KDamping * Vector3.Dot(velocities[spring.vector2s[0]] - velocities[spring.vector1s[0]], ((meshVertices[spring.vector2s[0]] - meshVertices[spring.vector1s[0]]) / Vector3.Distance(meshVertices[spring.vector2s[0]], meshVertices[spring.vector1s[0]])));

            Vector3 applyForce = ((meshVertices[spring.vector2s[0]] - meshVertices[spring.vector1s[0]]) / Vector3.Magnitude(meshVertices[spring.vector2s[0]] - meshVertices[spring.vector1s[0]])) * (springStretching + springDempening);

            forces[spring.vector1s[0]] += applyForce;
            forces[spring.vector2s[0]] -= applyForce;
        }

        IntegrationStepVerlet();

        SatisfyConstraints();

        mesh.vertices = meshVertices;

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        gameObject.GetComponent<MeshCollider>().sharedMesh = mesh;
    }

    /// <summary>
    /// Euler based integration (Old) </summary>
    private Vector3[] OldIntegrationStepEuler(Vector3[] meshVertices, Spring spring)
    {
        //V1
        meshVertices[spring.vector1s[0]] = meshVertices[spring.vector1s[0]] + velocities[spring.vector1s[0]] * Time.fixedDeltaTime;
        velocities[spring.vector1s[0]] = velocities[spring.vector1s[0]] + Time.fixedDeltaTime * forces[spring.vector1s[0]];

        //V2
        meshVertices[spring.vector2s[0]] = meshVertices[spring.vector2s[0]] + velocities[spring.vector2s[0]] * Time.fixedDeltaTime;
        velocities[spring.vector2s[0]] = velocities[spring.vector2s[0]] + Time.fixedDeltaTime * forces[spring.vector2s[0]];

        return meshVertices;
    }

    /// <summary>
    /// Verlet based integration (Using) </summary>
    private void IntegrationStepVerlet()
    {
        for (int i = 0; i < forces.Length; i++)
        {
            Vector3 tempStorageVertex1 = meshVertices[i];
            meshVertices[i] = 2 * meshVertices[i] - prevPos[i] + (forces[i] / Mass) * (Time.fixedDeltaTime * Time.fixedDeltaTime);
            velocities[i] = (meshVertices[i] - tempStorageVertex1) / Time.fixedDeltaTime;
            if (!staticVertices.Contains(i))
                prevPos[i] = tempStorageVertex1;
        }
    }

    /// <summary>
    /// Collision, elasticity and statics constraint checks in iterations
    /// </summary>
    private void SatisfyConstraints()
    {
        const int numIterations = 4;

        for (int i = 0; i < numIterations; i++)
        {
            for (int v = 0; v < meshVertices.Length; v++)
            {
                //Ground collision check
                if (transform.TransformPoint(meshVertices[v]).y < 0.2f)
                    meshVertices[v].y = transform.InverseTransformPoint(Vector3.zero).y + 0.2f;

                //Spheres (collisions) check
                for (int s = 0; s < _collidingSpheres.Count; s++)
                {
                    float distance = (transform.TransformPoint(meshVertices[v]) - _collidingSpheres[s].transform.position).magnitude;
                    float radius = _collidingSpheres[s].radius*_collidingSpheres[s].gameObject.transform.localScale.y;
                    if (distance < radius + 0.2f)
                    {
                        meshVertices[v] = transform.InverseTransformPoint(_collidingSpheres[s].transform.position + (transform.TransformPoint(meshVertices[v]) - _collidingSpheres[s].transform.position).normalized * (radius + 0.2f));
                        _collidingSpheres[s].attachedRigidbody.AddForce((_collidingSpheres[s].transform.position - transform.TransformPoint(meshVertices[v])).normalized * (radius + 0.2f));
                    }
                    else if (distance > (radius + 0.2f) * 30)
                    {
                        _collidingSpheres.Remove(_collidingSpheres[s]);
                    } 
                }
            }


            for (int j = 0; j < springs.Count; j++)
            {
                //Make the cloth less elastic
                Spring tempSpring = springs[j];
                Vector3 p0 = meshVertices[tempSpring.vector1s[0]];
                Vector3 p1 = meshVertices[tempSpring.vector2s[0]];
                Vector3 delta = p1 - p0;
                float len = Vector3.Magnitude(delta);
                float diff = (len - tempSpring.restLength) / len;
                p0 += delta * 0.5f * diff;
                p1 -= delta * 0.5f * diff;
                meshVertices[tempSpring.vector1s[0]] = p0;
                meshVertices[tempSpring.vector2s[0]] = p1;

                //Static vertices
                if (staticVertices.Contains(springs[j].vector1s[0]))
                {
                    meshVertices[springs[j].vector1s[0]] = prevPos[springs[j].vector1s[0]];
                }
                if (staticVertices.Contains(springs[j].vector2s[0]))
                {
                    meshVertices[springs[j].vector2s[0]] = prevPos[springs[j].vector1s[0]];
                }

                for (int k = 1; k <= springs[j].vector1s.Count - 1; k++)
                {
                    meshVertices[springs[j].vector1s[k]] = meshVertices[springs[j].vector1s[0]];
                }
                for (int k = 1; k <= springs[j].vector2s.Count - 1; k++)
                {
                    meshVertices[springs[j].vector2s[k]] = meshVertices[springs[j].vector2s[0]];
                }

            }

        }

    }

    /// <summary>
    /// Collisions trigger with the spheres used in the scene </summary>
    void OnCollisionEnter(Collision other)
    {
        if (other.gameObject.GetComponent<SphereCollider>())
        {
            if (!_collidingSpheres.Contains(other.gameObject.GetComponent<SphereCollider>()))
                _collidingSpheres.Add(other.gameObject.GetComponent<SphereCollider>());
        }
    }

    /// <summary>
    /// User clicked on a vertex, mainly for debugging reasons (stick vertex or give information) </summary>
    public void ClickedVertex(Vector3 vertexGlobalLocation, int option)
    {
        if (meshVertices == null) return;

        Vector3 closestVertex = transform.TransformPoint(meshVertices[0]);

        foreach (Vector3 meshVertex in meshVertices)
        {
            if (Vector3.Distance(vertexGlobalLocation, transform.TransformPoint(meshVertex)) <
                Vector3.Distance(vertexGlobalLocation, closestVertex))
            {
                closestVertex = transform.TransformPoint(meshVertex);
            }
        }


        for (int i = 0; i < springs.Count; i++)
        {
            if (transform.InverseTransformPoint(closestVertex) == meshVertices[springs[i].vector1s[0]])
            {
                if (option == 0)
                    staticVertices.Add(springs[i].vector1s[0]);
                if (option == 1)
                    Debug.Log("You just clicked on vertex: " + closestVertex + " which has number: " + springs[i].vector1s[0]);
                return;
            }
            if (transform.InverseTransformPoint(closestVertex) == meshVertices[springs[i].vector2s[0]])
            {
                if (option == 0)
                    staticVertices.Add(springs[i].vector2s[0]);
                if (option == 1)
                    Debug.Log("You just clicked on vertex: " + closestVertex + " which has number: " + springs[i].vector2s[0]);
                return;
            }
        }

    }

    /// <summary>
    /// Debug-Draw the springs of this model </summary>
    private void DrawSprings(float time)
    {
        foreach (Spring spring in springs)
        {
            Debug.DrawLine(mesh.vertices[spring.vector1s[0]], mesh.vertices[spring.vector2s[0]], Color.green, time, false);
        }
    }
}
