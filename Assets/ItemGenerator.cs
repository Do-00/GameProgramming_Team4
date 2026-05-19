using UnityEngine;

public class ItemGenerator : MonoBehaviour
{
    public GameObject applePrefab;
    public GameObject bombPrefab;
    public GameObject StarPrefab;
    float span = 1.0f;
    float delta = 0;
    int ratio = 3;
    float speed = -0.03f;

    public void Setparameter(float span, float speed, int ratio)
    {
        this.span = span;
        this.speed = speed;
        this.ratio = ratio;
    }

    void Update()
    {
        this.delta += Time.deltaTime;
        if (this.delta > this.span)
        {
            this.delta = 0;
            GameObject item;
            int dice = Random.Range(1, 11);
            if (dice < this.ratio)
            {
                item = Instantiate(bombPrefab);
            }
            else if (dice == this.ratio) 
            {
                item = Instantiate(StarPrefab);
            }
            else
            {
                item = Instantiate(applePrefab);
            }
            float x = Random.Range(-1, 2);
            float z = Random.Range(-1,2);
            item.transform.position = new Vector3(x,4,z);
            item.GetComponent<ItemController>().dropSpeed = this.speed;
        }
    }
}